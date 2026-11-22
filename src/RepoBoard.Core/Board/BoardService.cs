using System.Collections.Immutable;
using RepoBoard.Core.Git;
using RepoBoard.Core.Repos;
using RepoBoard.Core.Resources;
using RepoBoard.Core.Sync;
using RepoBoard.Core.IO;

namespace RepoBoard.Core.Board;

/// <summary>
/// The board: the repositories, their state and every git operation on them. One operation per repository at a time (its
/// gate), at most <see cref="MaxParallelNetwork"/> network operations at once. <see cref="Changed"/> fires from any
/// thread; the UI marshals itself.
/// </summary>
public sealed class BoardService(GitClient git, ConfigStore store, TimeProvider time)
{
    public const int MaxParallelNetwork = 4;

    /// <summary>Status reads at once – the board's and the file watcher's (review 0.4.0, B6).</summary>
    public const int MaxParallelReads = 8;
    /// <summary>The commits an opened row shows at first; "Load more" adds <see cref="LogStep"/> up to <see cref="MaxLogLength"/>.</summary>
    public const int LogLength = 15;

    public const int LogStep = 50;
    public const int MaxLogLength = 1000;

    private readonly SemaphoreSlim _network = new(MaxParallelNetwork);
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly Lock _settingsLock = new();
    private ImmutableList<RepoState> _repos = [];
    private int _autoSyncMinutes = BoardConfig.DefaultAutoSyncMinutes;
    private AppSettings _settings = new();
    private volatile bool _autoSyncPaused;
    private int _autoSyncRuns;
    private bool _canSave = true;

    public event Action? Changed;

    /// <summary>After each run of <see cref="AutoSyncDueAsync"/> that synced something, on the thread that ran it.</summary>
    public event Action? AutoSyncRunEnded;

    public ActivityLog Activity { get; } = new();
    public IReadOnlyList<RepoState> Repos => _repos;
    public int AutoSyncMinutes => _autoSyncMinutes;
    public AppSettings Settings => _settings;

    /// <summary>Auto-sync skips its runs (tray menu); the status is still read. For this run only (decision of the user).</summary>
    public bool AutoSyncPaused => _autoSyncPaused;

    /// <summary>A run of <see cref="AutoSyncDueAsync"/> is under way: what it logs belongs together (one notification).</summary>
    public bool AutoSyncRunning => Volatile.Read(ref _autoSyncRuns) > 0;

    public async Task LoadAsync(CancellationToken ct)
    {
        var load = await store.LoadAsync(ct);
        _canSave = load.CanSave;
        _autoSyncMinutes = load.Config.AutoSyncMinutes;
        _settings = load.Config.Settings;
        _repos = [.. load.Config.Repos.Select(e => new RepoState(e)).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];
        if (load.Problem is { } problem)
        {
            Log(AppPaths.AppFolderName, problem, ActivityKind.Error);
        }

        Notify();
    }

    // ---------- the list ----------

    /// <summary>Adds the repositories the paths are in; returns how many were new.</summary>
    public async Task<int> AddAsync(IEnumerable<string> paths, CancellationToken ct)
    {
        var added = new List<RepoState>();
        foreach (var path in paths)
        {
            var (found, problem) = await git.FindRootAsync(path, ct);
            if (found is not { } root)
            {
                Log(Path.GetFileName(path), problem is null
                    ? TextFormat.Format(SyncText.NotARepository, path)
                    : TextFormat.Format(SyncText.Add_Refused, path, problem), ActivityKind.Error);
                continue;
            }

            // A linked worktree shows under its repository (WP-04): that one goes on the board.
            string? worktree = null;
            if (await MainOfLinkedWorktreeAsync(root, ct) is { } main && !SamePath(main, root))
            {
                (worktree, root) = (root, main);
            }

            var repo = new RepoState(new RepoEntry(root));
            var exists = false;
            ImmutableInterlocked.Update(ref _repos, list =>
            {
                exists = list.Any(r => SamePath(r.Path, root));
                return exists ? list : [.. list.Add(repo).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];
            });
            if (!exists)
            {
                added.Add(repo);
                if (worktree is not null)
                {
                    Log(Path.GetFileName(worktree), TextFormat.Format(SyncText.Worktree_AddedMain, worktree, root), ActivityKind.Info);
                }
            }
        }

        if (added.Count > 0)
        {
            await SaveAsync(ct);
            Notify();
            await Task.WhenAll(added.Select(async r =>
            {
                await RefreshAsync(r, ct);
                await RefreshWorktreesAsync(r, ct);
            }));
        }

        return added.Count;
    }

    /// <summary>
    /// The repositories below <paramref name="root"/> that are not on the board yet, read without git (review 0.4.0, A9):
    /// a repository's configuration and hooks run with every git call RepoBoard makes there, so the user ticks first.
    /// </summary>
    public Task<IReadOnlyList<ScanCandidate>> ScanAsync(string root, CancellationToken ct) => Task.Run(() =>
        (IReadOnlyList<ScanCandidate>)[.. RepoScanner.Candidates(root)
            .Where(c => !_repos.Any(r => Paths.Same(r.Path, c.Board)))
            .DistinctBy(c => Paths.Full(c.Board), Paths.Comparer)], ct);

    /// <summary>Adds the repositories the user ticked from <see cref="ScanAsync"/> and logs how many were new.</summary>
    public async Task<int> AddScannedAsync(string root, IReadOnlyList<ScanCandidate> chosen, CancellationToken ct)
    {
        var added = chosen.Count == 0 ? 0 : await AddAsync(chosen.Select(c => c.Path), ct);
        Log(Path.GetFileName(Path.TrimEndingDirectorySeparator(root)), added == 0 ? SyncText.Scan_NothingNew : TextFormat.Plural(added, SyncText.Scan_AddedOne, SyncText.Scan_AddedOther), ActivityKind.Info);
        Notify();
        return added;
    }

    /// <summary>Takes a repository off the board (the folder stays); the activity log says so, with its path (review 0.4.0, A10).</summary>
    public async Task RemoveAsync(RepoState repo, CancellationToken ct)
    {
        ImmutableInterlocked.Update(ref _repos, list => list.Remove(repo));
        Log(repo.Name, TextFormat.Format(SyncText.RemovedFromBoard, repo.Path), ActivityKind.Info);
        await SaveAsync(ct);
        Notify();
    }

    /// <summary>Auto-pull without auto-fetch makes no sense: switching one keeps the pair consistent (ADR 0003).</summary>
    public Task SetAutoFetchAsync(RepoState repo, bool on, CancellationToken ct) =>
        UpdateEntryAsync(repo, repo.Entry with { AutoFetch = on, AutoPull = on && repo.Entry.AutoPull }, ct);

    /// <inheritdoc cref="SetAutoFetchAsync"/>
    public Task SetAutoPullAsync(RepoState repo, bool on, CancellationToken ct) =>
        UpdateEntryAsync(repo, repo.Entry with { AutoPull = on, AutoFetch = on || repo.Entry.AutoFetch }, ct);

    public async Task SetAutoSyncMinutesAsync(int minutes, CancellationToken ct)
    {
        _autoSyncMinutes = BoardConfig.Intervals.Contains(minutes) ? minutes : BoardConfig.DefaultAutoSyncMinutes;
        await SaveAsync(ct);
        Notify();
    }

    /// <summary>
    /// From any thread: the update check saves which version it announced from the thread pool while the user may switch a
    /// setting – read and change under one lock, or one of the two is lost (Review WP-10).
    /// </summary>
    public async Task UpdateSettingsAsync(Func<AppSettings, AppSettings> change, CancellationToken ct)
    {
        lock (_settingsLock)
        {
            _settings = change(_settings);
        }

        await SaveAsync(ct);
        Notify();
    }

    public void SetAutoSyncPaused(bool paused)
    {
        if (_autoSyncPaused == paused)
        {
            return;
        }

        _autoSyncPaused = paused;
        Log(AppPaths.AppFolderName, paused ? SyncText.AutoSync_Paused : SyncText.AutoSync_Resumed, ActivityKind.Info);
        Notify();
    }

    private async Task UpdateEntryAsync(RepoState repo, RepoEntry entry, CancellationToken ct)
    {
        var switchedOn = (entry.AutoFetch && !repo.Entry.AutoFetch) || (entry.AutoPull && !repo.Entry.AutoPull);
        repo.Entry = entry;
        if (switchedOn)
        {
            repo.LastAutoSync = null; // due on the next tick, not after a whole interval
        }

        if (!entry.AutoPull)
        {
            repo.AutoPullBlocked = null;
        }

        await SaveAsync(ct);
        Notify();
    }

    /// <summary>Not over a file this version could not read (newer, locked): the change holds until the app ends.</summary>
    private async Task SaveAsync(CancellationToken ct)
    {
        if (!_canSave)
        {
            return;
        }

        await _saveGate.WaitAsync(ct);
        try
        {
            await store.SaveAsync(new BoardConfig { Repos = [.. _repos.Select(r => r.Entry)], AutoSyncMinutes = _autoSyncMinutes, Settings = _settings }, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log(AppPaths.AppFolderName, TextFormat.Format(CoreText.Config_NotSaved, store.File, ex.Message), ActivityKind.Error);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    // ---------- reading the state ----------

    public async Task SetWantsDetailsAsync(RepoState repo, bool wants, CancellationToken ct)
    {
        repo.WantsDetails = wants;
        if (wants)
        {
            await RefreshAsync(repo, ct);
        }
        else
        {
            repo.Details = null;
            repo.LogLength = LogLength;
            Notify();
        }
    }

    /// <summary>
    /// What the opened row's history shows (WP-09): <paramref name="length"/> commits (clamped to <see cref="MaxLogLength"/>),
    /// of HEAD only or of all branches. Read again right away – or, while an operation runs, when it ends.
    /// </summary>
    public async Task SetLogAsync(RepoState repo, int length, bool all, CancellationToken ct)
    {
        repo.LogLength = Math.Clamp(length, LogLength, MaxLogLength);
        repo.LogAll = all;
        // Waits like a click: a status read under way may have read the history already and would keep the old one, up to
        // two minutes for a quiet watched repository (review 0.4.0, A13).
        await repo.Gate.WaitAsync(ct);
        try
        {
            await RefreshCoreAsync(repo, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            repo.Error = TextFormat.Format(SyncText.OperationFailed, SyncText.Op_Status, ex.Message);
            repo.StatusFailed = true;
        }
        finally
        {
            repo.Gate.Release();
            Notify();
        }
    }

    /// <summary>The repositories first – that reads their lists of worktrees –, then every worktree.</summary>
    public Task RefreshAllAsync(CancellationToken ct) => RefreshManyAsync(_ => true, ct);

    /// <summary>
    /// The status tick (WP-03, ADR 0011): every repository and worktree no file watcher sees, and the watched ones whose
    /// last status is <see cref="WatchedRefresh"/> old – a fallback for an event the watcher missed.
    /// </summary>
    public Task RefreshStaleAsync(CancellationToken ct) => RefreshManyAsync(IsStale, ct);

    /// <summary>The repositories first – that reads their lists of worktrees –, then the worktrees; each only if <paramref name="which"/>.</summary>
    private async Task RefreshManyAsync(Func<RepoState, bool> which, CancellationToken ct)
    {
        await EachAsync(_repos.Where(which), MaxParallelReads, RefreshAsync, ct);
        await EachAsync(_repos.SelectMany(r => r.Worktrees).Where(which), MaxParallelReads, RefreshAsync, ct);
    }

    /// <summary>How often the tick reads a repository a file watcher sees (decision of the user, 2026-11-21).</summary>
    public static readonly TimeSpan WatchedRefresh = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How old a status of a repository no watcher sees must be for the tick to read it again: half a tick, so a status just
    /// read by someone else – at the start the window's activation and the watchers' first reads – is not read a second
    /// time right after (review 0.4.0, A14).
    /// </summary>
    public static readonly TimeSpan UnwatchedRefresh = TimeSpan.FromSeconds(10);

    /// <summary>A repository whose last status failed is read on every tick, as without a watcher.</summary>
    private bool IsStale(RepoState repo) =>
        repo.StatusFailed || repo.StatusReadAt is not { } read || time.GetElapsedTime(read) >= (repo.Watched ? WatchedRefresh : UnwatchedRefresh);

    /// <summary>After a fetch or pull in a repository: its worktrees share the refs, their ahead and behind changed too.</summary>
    private Task RefreshWorktreesAsync(RepoState repo, CancellationToken ct, RepoState? except = null) =>
        Task.WhenAll(repo.Worktrees.Where(w => w != except).Select(w => RefreshAsync(w, ct)));

    /// <summary>
    /// Reads the status again; skipped while an operation runs on the repository (it refreshes when done). False when
    /// skipped – the file watcher tries again (review WP-03).
    /// </summary>
    public async Task<bool> RefreshAsync(RepoState repo, CancellationToken ct)
    {
        if (!await repo.Gate.WaitAsync(0, ct))
        {
            return false;
        }

        try
        {
            await RefreshCoreAsync(repo, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One repository's surprise must not stop the status – and auto-sync – of all the others (review WP-04).
            repo.Error = TextFormat.Format(SyncText.OperationFailed, SyncText.Op_Status, ex.Message);
            repo.StatusFailed = true;
        }
        finally
        {
            repo.Gate.Release();
            Notify();
        }

        return true;
    }

    private async Task RefreshCoreAsync(RepoState repo, CancellationToken ct)
    {
        // Set only once the status is read: a failed read does not hide the change a watcher saw (review WP-03).
        var started = time.GetTimestamp();
        // A folder that lost its .git counts as missing too: git would find the repository around it and show – and
        // auto-sync, commit – that one (review 0.4.0, A6).
        if (!IsRepository(repo.Path))
        {
            repo.StatusReadAt = started;
            repo.StatusFailed = false;
            repo.Missing = true;
            repo.Status = null;
            repo.GitDir = null;
            repo.Worktrees = [];
            return;
        }

        repo.Missing = false;
        repo.GitDir ??= await git.GitDirAsync(repo.Path, ct);
        var (status, result) = await git.StatusAsync(repo.Path, ct);
        if (status is null)
        {
            repo.Error = TextFormat.Format(SyncText.OperationFailed, SyncText.Op_Status, result.Message);
            repo.StatusFailed = true;
            return;
        }

        // A clone of an empty repository has no commit, and git writes no ahead/behind for it – even once a colleague pushed
        // the first commit. Then it is that far behind, and auto-pull brings it (review 0.4.0, A18).
        if (status is { IsInitial: true, Upstream: { } upstream } && await git.CountAsync(repo.Path, upstream, ct) is > 0 and var behind)
        {
            status = status with { Behind = behind };
        }

        repo.Status = status;
        repo.StatusReadAt = started;
        repo.StatusFailed = false;
        if (repo.GitDir is { } dir)
        {
            repo.LastFetch = GitClient.LastFetchIn(dir);
            repo.Operation = GitClient.OperationIn(dir);
        }

        if (!repo.IsWorktree)
        {
            await ReadWorktreesAsync(repo, ct);
        }

        if (repo.WantsDetails)
        {
            var branches = await git.BranchesAsync(repo.Path, status.Branch, ct);
            var (log, more) = status.IsInitial ? ([], false) : await git.LogAsync(repo.Path, repo.LogLength, repo.LogAll, ct);
            // Unknown (null) keeps amend off as pushed does, with its own reason (review 0.4.0, A38).
            var onRemote = status.IsInitial ? true : await git.IsOnRemoteAsync(repo.Path, "HEAD", ct);
            repo.Details = new RepoDetails(branches, log, CommitGraph.Build(log), more, onRemote);
        }
    }

    /// <summary>
    /// The diff of one changed file for the opened row. Only reads: it does not wait for the repository's gate and leaves
    /// <see cref="RepoState.Error"/> alone – a failure is shown in place of the diff. Runs on the thread pool: the UI calls it
    /// from the renderer's context, where reading and parsing a large diff would stall the window (review WP-05).
    /// </summary>
    public Task<(FileDiff? Diff, string? Error)> DiffAsync(RepoState repo, FileChange change, bool staged, CancellationToken ct) =>
        ReadDiffAsync(() => git.DiffAsync(repo.Path, change, staged, ct), ct);

    /// <summary>A diff read on the thread pool; a failure as the text shown in its place.</summary>
    private static async Task<(FileDiff? Diff, string? Error)> ReadDiffAsync(Func<Task<(FileDiff? Diff, GitResult Result)>> read, CancellationToken ct)
    {
        try
        {
            var (diff, result) = await Task.Run(read, ct);
            return diff is not null ? (diff, null) : (null, TextFormat.Format(SyncText.OperationFailed, SyncText.Op_Diff, result.Message));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, TextFormat.Format(SyncText.OperationFailed, SyncText.Op_Diff, ex.Message));
        }
    }

    /// <summary>
    /// One commit of the history with its files (WP-09) and whether HEAD contains it. Only reads, like <see cref="DiffAsync"/>:
    /// no gate, a failure is shown in place of the details.
    /// </summary>
    public async Task<(CommitDetails? Commit, string? Error)> CommitDetailsAsync(RepoState repo, string id, CancellationToken ct)
    {
        try
        {
            return await Task.Run(async () =>
            {
                var (commit, result) = await git.CommitDetailsAsync(repo.Path, id, ct);
                if (commit is null)
                {
                    return (null, TextFormat.Format(SyncText.OperationFailed, SyncText.Op_Show, result.Message));
                }

                var inHead = repo.Status is { IsInitial: false } && await git.IsAncestorAsync(repo.Path, commit.Id, "HEAD", ct) == true;
                return ((CommitDetails?)(commit with { InHead = inHead }), (string?)null);
            }, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, TextFormat.Format(SyncText.OperationFailed, SyncText.Op_Show, ex.Message));
        }
    }

    /// <summary>
    /// Whether HEAD already has the change of <paramref name="commit"/> (<see cref="GitClient.IsAppliedAsync"/>), for the
    /// cherry-pick button. Read after the details are shown: <c>git cherry</c> computes a patch id for every commit of HEAD
    /// since the fork point, which takes a while for an old branch (review WP-09). Only reads.
    /// </summary>
    public Task<bool> IsAppliedAsync(RepoState repo, CommitDetails commit, CancellationToken ct) =>
        commit.InHead || commit.IsMerge || repo.Status is not { IsInitial: false }
            ? Task.FromResult(false)
            : Task.Run(async () => await git.IsAppliedAsync(repo.Path, commit, ct) == true, ct);

    /// <summary>The diff of one file of a commit (WP-09); only reads, on the thread pool (as <see cref="DiffAsync"/>).</summary>
    public Task<(FileDiff? Diff, string? Error)> CommitDiffAsync(RepoState repo, CommitDetails commit, CommitFile file, CancellationToken ct) =>
        ReadDiffAsync(() => git.CommitDiffAsync(repo.Path, commit, file, ct), ct);

    /// <summary>The whole message of HEAD, to start an amend from (WP-09); null if there is none.</summary>
    public Task<string?> HeadMessageAsync(RepoState repo, CancellationToken ct) =>
        repo.Status is { IsInitial: false } ? Task.Run(() => git.MessageAsync(repo.Path, "HEAD", ct), ct) : Task.FromResult<string?>(null);

    // ---------- operations (clicks) ----------

    /// <summary>
    /// "Fetch all" – the button on the page and the tray's menu. No more at once than the network takes: one waiting in line
    /// would hold its repository's gate meanwhile (review 0.4.0, A3); a missing one has nothing to fetch (A17).
    /// </summary>
    public Task FetchAllAsync(CancellationToken ct) => EachAsync(_repos.Where(r => !r.Missing), MaxParallelNetwork, FetchAsync, ct);

    /// <summary>
    /// <paramref name="work"/> for each repository, <paramref name="parallel"/> at once. Each runs to its end with the caller's
    /// token: <c>Parallel.ForEachAsync</c> cancels its own token at the first failure, and that killed git in every other
    /// repository – a fast-forward mid-checkout, leaving <c>index.lock</c> behind (second review pass R1). A failure stays in
    /// its repository's row.
    /// </summary>
    private Task EachAsync<T>(IEnumerable<RepoState> repos, int parallel, Func<RepoState, CancellationToken, Task<T>> work, CancellationToken ct) =>
        Parallel.ForEachAsync(repos, new ParallelOptions { MaxDegreeOfParallelism = parallel, CancellationToken = ct }, async (repo, _) =>
        {
            try
            {
                await work(repo, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                repo.Error = TextFormat.Format(SyncText.OperationFailed, SyncText.Op_Status, ex.Message);
                repo.StatusFailed = true;
                Notify();
            }
        });

    public Task<bool> FetchAsync(RepoState repo, CancellationToken ct) =>
        RunAsync(repo, SyncText.Op_Fetch, network: true, c => git.FetchAsync(repo.Path, GitCallMode.Interactive, c),
            () => Info(repo.Status is { HasUpstream: true, Behind: > 0 } s
                ? TextFormat.Format(SyncText.Fetched_Behind, Commits(s.Behind), s.Upstream)
                : SyncText.Fetched_UpToDate),
            ct);

    public async Task<bool> PullAsync(RepoState repo, CancellationToken ct)
    {
        var before = repo.Status?.Head;
        ActivityNote? pulled = null;
        return await RunAsync(repo, SyncText.Op_Pull, network: true, async c =>
        {
            var result = await git.PullAsync(repo.Path, c);
            pulled = result.Success ? await PulledTextAsync(repo.Path, before, c) : null;
            return result;
        }, () => pulled, ct);
    }

    public Task<bool> PushAsync(RepoState repo, CancellationToken ct)
    {
        var setUpstream = repo.Status is { HasUpstream: false, IsDetached: false };
        return RunAsync(repo, SyncText.Op_Push, network: true, c => git.PushAsync(repo.Path, setUpstream, c),
            () => Info(setUpstream
                ? TextFormat.Format(SyncText.PushedWithUpstream, repo.Status?.Upstream)
                : TextFormat.Format(SyncText.Pushed, repo.Status?.Branch)),
            ct);
    }

    /// <summary>Stages <paramref name="paths"/>; all changes when empty.</summary>
    public Task<bool> StageAsync(RepoState repo, IReadOnlyList<string> paths, CancellationToken ct) =>
        RunAsync(repo, SyncText.Op_Stage, network: false,
            c => paths.Count == 0 ? git.StageAllAsync(repo.Path, c) : git.StageAsync(repo.Path, paths, c), null, ct);

    /// <summary>
    /// Unstages <paramref name="paths"/>; all when empty. A staged rename is unstaged with its original path, or the index
    /// would keep the old path's deletion and lose the new file (review WP-25).
    /// </summary>
    public Task<bool> UnstageAsync(RepoState repo, IReadOnlyList<string> paths, CancellationToken ct)
    {
        var originals = repo.Status?.Changes
            .Where(c => c.IsStaged && c.OriginalPath is not null && paths.Contains(c.Path))
            .Select(c => c.OriginalPath!) ?? [];
        IReadOnlyList<string> all = paths.Count == 0 ? ["."] : [.. paths, .. originals];
        return RunAsync(repo, SyncText.Op_Unstage, network: false,
            c => git.UnstageAsync(repo.Path, all, repo.Status?.IsInitial == true, c), null, ct);
    }

    /// <summary>
    /// Stages (or, from the staged side, unstages) one hunk of <paramref name="diff"/> or the selected lines in it (WP-25,
    /// ADR 0012). Writes only to the index. Inside the repository's gate the diff is read again: if it is not the one the
    /// user sees (the file changed since), nothing is staged and the new status brings the current diff; a patch is built
    /// only from the file's own text (<c>--no-textconv</c>) – a diff driver's text in the index would replace the file, a
    /// git-crypt secret with its plain text (review WP-25).
    /// </summary>
    /// <param name="lines">Indexes of the selected lines in the hunk; null for the whole hunk.</param>
    public Task<bool> StageHunkAsync(RepoState repo, FileChange change, FileDiff diff, bool staged, int hunk,
        IReadOnlySet<int>? lines, CancellationToken ct)
    {
        var label = (staged, lines is null) switch
        {
            (false, true) => SyncText.Op_StageHunk,
            (false, false) => SyncText.Op_StageLines,
            (true, true) => SyncText.Op_UnstageHunk,
            (true, false) => SyncText.Op_UnstageLines,
        };
        // On the thread pool: the click comes from the renderer's context, and reading, parsing and comparing a large diff
        // twice would stall the window (as in DiffAsync; second review pass WP-25).
        return RunAsync(repo, label, network: false, c => Task.Run(() => StageHunkCoreAsync(repo, change, diff, staged, hunk, lines, c), c),
            null, ct);
    }

    private async Task<GitResult> StageHunkCoreAsync(RepoState repo, FileChange change, FileDiff diff, bool staged, int hunk,
        IReadOnlySet<int>? lines, CancellationToken c)
    {
        // Unstaging a rename as a whole would take the rename back too: the patch keeps it and takes out the hunk.
        var patch = HunkPatch.Build(change.Path, diff, hunk, lines, reverse: staged, wholeFile: !(staged && change.OriginalPath is not null));
        var (current, read) = await git.DiffAsync(repo.Path, change, staged, c);
        if (current is null)
        {
            return read;
        }

        if (!current.SameAs(diff))
        {
            return GitResult.Failed(SyncText.Hunk_Changed);
        }

        if (patch.Kind == HunkPatchKind.Patch)
        {
            var (own, readOwn) = await git.DiffAsync(repo.Path, change, staged, c, textconv: false);
            if (own is null)
            {
                return readOwn;
            }

            if (!own.SameAs(diff))
            {
                return GitResult.Failed(SyncText.Hunk_DiffDriver);
            }
        }

        return patch.Kind switch
        {
            HunkPatchKind.Patch => await git.ApplyToIndexAsync(repo.Path, patch, reverse: staged, c),
            HunkPatchKind.WholeFile => staged
                ? await git.UnstageAsync(repo.Path, [change.Path], repo.Status?.IsInitial == true, c)
                : await git.StageAsync(repo.Path, [change.Path], c),
            HunkPatchKind.Nothing => new GitResult(0, "", ""),
            HunkPatchKind.Incomplete => GitResult.Failed(SyncText.Hunk_Incomplete),
            HunkPatchKind.NotUtf8 => GitResult.Failed(SyncText.Hunk_NotUtf8),
            HunkPatchKind.SpecialFile => GitResult.Failed(SyncText.Hunk_SpecialFile),
            HunkPatchKind.TwoFiles => GitResult.Failed(SyncText.Hunk_TwoFiles),
            _ => GitResult.Failed(SyncText.Hunk_SplitsLastLine),
        };
    }

    /// <summary>
    /// Commits what is staged; with nothing staged, everything (like "commit all" elsewhere). With <paramref name="amend"/>
    /// (WP-09) the staged changes and the message replace the last commit instead – nothing is staged on top. Checked inside
    /// the gate right before: no remote branch has that commit (it would need a force push), no merge or rebase is under
    /// way (a stopped rebase would take the next commit's changes into it), and HEAD is still
    /// <paramref name="expectedHead"/>, the commit whose message the user started from – another session may have committed
    /// meanwhile (review WP-09).
    /// </summary>
    public Task<bool> CommitAsync(RepoState repo, string message, bool push, CancellationToken ct, bool amend = false,
        string? expectedHead = null)
    {
        var stageAll = !amend && repo.Status is { Staged: 0 };
        var setUpstream = repo.Status is { HasUpstream: false, IsDetached: false };
        var label = (amend, push) switch
        {
            (false, false) => SyncText.Op_Commit,
            (false, true) => SyncText.Op_CommitPush,
            (true, false) => SyncText.Op_Amend,
            (true, true) => SyncText.Op_AmendPush,
        };
        var note = (amend, push) switch
        {
            (false, false) => SyncText.Committed,
            (false, true) => SyncText.CommittedAndPushed,
            (true, false) => SyncText.Amended,
            (true, true) => SyncText.AmendedAndPushed,
        };
        return RunAsync(repo, label, network: push, async c =>
        {
            if (stageAll && await git.StageAllAsync(repo.Path, c) is { Success: false } staged)
            {
                return staged;
            }

            GitResult committed;
            if (amend)
            {
                if (await AmendBlockerAsync(repo, expectedHead, c) is { } blocked)
                {
                    return blocked;
                }

                committed = await git.AmendAsync(repo.Path, message, c);
            }
            else
            {
                committed = await git.CommitAsync(repo.Path, message, c);
            }

            return !committed.Success || !push ? committed : await git.PushAsync(repo.Path, setUpstream, c);
        }, () => Info(TextFormat.Format(note, CommitMessage.Subject(message))), ct);
    }

    /// <summary>Why the last commit must not be amended right now (inside the gate); null if it may.</summary>
    private async Task<GitResult?> AmendBlockerAsync(RepoState repo, string? expectedHead, CancellationToken c)
    {
        var gitDir = await git.GitDirAsync(repo.Path, c);
        if (gitDir is null)
        {
            return GitResult.Failed(SyncText.Pick_Unknown);
        }

        if (GitClient.OperationIn(gitDir) is { } running)
        {
            return GitResult.Failed(TextFormat.Format(SyncText.Pick_Operation, PullCheck.Name(running)));
        }

        if (expectedHead is not null && await git.CommitOfAsync(repo.Path, "HEAD", c) != expectedHead)
        {
            return GitResult.Failed(SyncText.Amend_HeadMoved);
        }

        return PickCheck.OnRemote(await git.IsOnRemoteAsync(repo.Path, "HEAD", c)) is { } pushed ? GitResult.Failed(pushed) : null;
    }

    /// <summary>A new commit that undoes <paramref name="commit"/> (WP-09); see <see cref="PickAsync"/>.</summary>
    public Task<bool> RevertAsync(RepoState repo, CommitDetails commit, CancellationToken ct) =>
        RunAsync(repo, SyncText.Op_Revert, network: false, c => PickAsync(repo, commit, revert: true, c),
            () => Info(TextFormat.Format(SyncText.Reverted, commit.ShortId, commit.Subject)), ct);

    /// <summary>Applies <paramref name="commit"/> onto HEAD as a new commit (WP-09); see <see cref="PickAsync"/>.</summary>
    public Task<bool> CherryPickAsync(RepoState repo, CommitDetails commit, CancellationToken ct) =>
        RunAsync(repo, SyncText.Op_CherryPick, network: false, c => PickAsync(repo, commit, revert: false, c),
            () => Info(TextFormat.Format(SyncText.CherryPicked, commit.ShortId, commit.Subject, repo.Status?.Branch ?? "HEAD")), ct);

    /// <summary>
    /// Revert or cherry-pick (WP-09, ADR 0013), checked inside the gate right before git runs: no unfinished operation, no
    /// staged, changed or conflicted file (so that a later abort of a stopped one loses nothing of the user's; new files
    /// may stay – git refuses to overwrite one), no ignored file at a path of the commit (git overwrites those without a
    /// word; review WP-09), not a merge commit, a revert only of a commit HEAD contains and a cherry-pick only of one it
    /// does not, nor its change (<see cref="GitClient.IsAppliedAsync"/>). If git stops – at a conflict, or with a
    /// cherry-pick that came out empty – the operation stays unfinished for the IDE or terminal (decision of the user,
    /// 2026-11-22) and the message says how to go on.
    /// </summary>
    private async Task<GitResult> PickAsync(RepoState repo, CommitDetails commit, bool revert, CancellationToken c)
    {
        if (commit.IsMerge)
        {
            return GitResult.Failed(SyncText.Pick_Merge);
        }

        var (status, read) = await git.StatusAsync(repo.Path, c);
        if (status is null)
        {
            return read;
        }

        var gitDir = await git.GitDirAsync(repo.Path, c);
        if (gitDir is null)
        {
            return GitResult.Failed(SyncText.Pick_Unknown);
        }

        if (PickCheck.Before(commit.IsMerge, status, GitClient.OperationIn(gitDir)) is { } before)
        {
            return GitResult.Failed(before);
        }

        var inHead = await git.IsAncestorAsync(repo.Path, commit.Id, "HEAD", c);
        if (inHead is null)
        {
            return GitResult.Failed(SyncText.Pick_Unknown);
        }

        // An empty revert fails without leaving anything behind; an empty cherry-pick would stay unfinished (E2E WP-09).
        var applied = !revert && !inHead.Value && await git.IsAppliedAsync(repo.Path, commit, c) == true;
        if (PickCheck.Side(revert, inHead.Value, applied) is { } side)
        {
            return GitResult.Failed(side);
        }

        if (await IgnoredInTheWayAsync(repo, commit, c) is { } blocked)
        {
            return blocked;
        }

        var result = revert ? await git.RevertAsync(repo.Path, commit.Id, c) : await git.CherryPickAsync(repo.Path, commit.Id, c);
        if (result.Success || GitClient.OperationIn(gitDir) is null)
        {
            return result;
        }

        // Stopped without a conflict and with nothing to commit (the worktree was clean before): the change is already in
        // HEAD in another form, as after a squash merge – "git cherry" only knows 1:1 copies (review WP-09).
        var (after, _) = await git.StatusAsync(repo.Path, c);
        return GitResult.Failed(!revert && after is { Conflicts: 0 } && after.Changes.All(f => f.Untracked)
            ? SyncText.CherryPick_Empty
            : TextFormat.Format(revert ? SyncText.Revert_Stopped : SyncText.CherryPick_Stopped, result.Message));
    }

    /// <summary>
    /// An ignored file git would overwrite without a word (review WP-09): at a path of the commit (also inside an ignored
    /// folder where the commit needs a file – the path as pathspec finds the folder's files), or where the commit needs a
    /// folder (<c>foo</c> for <c>foo/bar</c>; second review pass). For a folder only a file of exactly that name counts: an
    /// ignored folder above a path (<c>node_modules</c>) is no obstacle. Null if nothing is in the way.
    /// </summary>
    private async Task<GitResult?> IgnoredInTheWayAsync(RepoState repo, CommitDetails commit, CancellationToken c)
    {
        string[] paths = [.. commit.Files.SelectMany(f => f.OriginalPath is { } from ? [from, f.Path] : new[] { f.Path }).Distinct()];
        // Only folders where a file lies in the worktree: mostly none, so git is asked for few of them.
        string[] files = [.. paths.SelectMany(Folders).Distinct().Where(f => File.Exists(Path.Combine(repo.Path, f)))];
        if (paths.Length == 0)
        {
            return null; // without a pathspec git would list every ignored file
        }

        var ignored = await git.IgnoredAmongAsync(repo.Path, [.. paths, .. files], c);
        if (ignored is null)
        {
            return GitResult.Failed(SyncText.Pick_Unknown);
        }

        var hit = ignored.FirstOrDefault(i => files.Contains(i)
            || paths.Any(p => i == p || i.StartsWith(p + "/", StringComparison.Ordinal)));
        return hit is null ? null : GitResult.Failed(TextFormat.Format(SyncText.Pick_Ignored, hit));

        // "a/b/c" → "a", "a/b" (git writes paths with "/" on every platform).
        static IEnumerable<string> Folders(string path)
        {
            for (var i = path.IndexOf('/'); i > 0; i = path.IndexOf('/', i + 1))
            {
                yield return path[..i];
            }
        }
    }

    /// <summary>
    /// A local branch by name; a remote one ("upstream/main") as a new local branch that tracks exactly it. git's guess from
    /// the bare name took the local "main" of origin, a local branch of the same name, or failed on two remotes (review
    /// 0.4.0, A5); now git refuses clearly when the local name is taken.
    /// </summary>
    public Task<bool> SwitchAsync(RepoState repo, BranchInfo branch, CancellationToken ct) =>
        RunAsync(repo, SyncText.Op_Switch, network: false, async c =>
        {
            if (!branch.IsRemote)
            {
                return await CheckedAsync(repo, branch.Name, git.SwitchAsync, c);
            }

            if (GitClient.LocalNameOf(branch.Name, await git.RemotesAsync(repo.Path, c)) is not { } local)
            {
                return GitResult.Failed(TextFormat.Format(SyncText.InvalidBranchName, branch.Name));
            }

            return await git.IsBranchNameAsync(repo.Path, branch.Name, c)
                ? await CheckedAsync(repo, local, (path, name, token) => git.SwitchTrackingAsync(path, name, branch.Name, token), c)
                : GitResult.Failed(TextFormat.Format(SyncText.InvalidBranchName, branch.Name));
        }, () => Info(TextFormat.Format(SyncText.Switched, repo.Status?.Branch ?? branch.Name)), ct);

    public Task<bool> CreateBranchAsync(RepoState repo, string name, CancellationToken ct) =>
        RunAsync(repo, SyncText.Op_CreateBranch, network: false, c => CheckedAsync(repo, name, git.CreateBranchAsync, c),
            () => Info(TextFormat.Format(SyncText.BranchCreated, name)), ct);

    /// <summary>A branch name typed by the user or read from a ref goes to git only once git accepts it as one.</summary>
    private async Task<GitResult> CheckedAsync(RepoState repo, string name, Func<string, string, CancellationToken, Task<GitResult>> run, CancellationToken ct) =>
        await git.IsBranchNameAsync(repo.Path, name, ct)
            ? await run(repo.Path, name, ct)
            : GitResult.Failed(TextFormat.Format(SyncText.InvalidBranchName, name));

    public void ClearError(RepoState repo)
    {
        repo.Error = null;
        Notify();
    }

    /// <summary>A failure outside git: a program that does not start, a dialog that fails.</summary>
    public void ReportError(string source, string text)
    {
        Log(source, text, ActivityKind.Error);
        Notify();
    }

    // ---------- auto-sync (ADR 0003) ----------

    /// <summary>
    /// Syncs every repository whose interval has passed; nothing while paused. Called by <see cref="AutoSyncService"/> on
    /// each tick.
    /// </summary>
    public async Task AutoSyncDueAsync(CancellationToken ct)
    {
        if (_autoSyncPaused)
        {
            return;
        }

        var now = time.GetUtcNow();
        var interval = TimeSpan.FromMinutes(_autoSyncMinutes);
        var due = _repos.Where(r => r.AutoSyncOn && !r.Missing && (r.LastAutoSync is null || now - r.LastAutoSync >= interval)).ToList();
        if (due.Count == 0)
        {
            return;
        }

        Interlocked.Increment(ref _autoSyncRuns);
        try
        {
            // No more at once than the network takes, one slot left for a click: a repository waiting in line would hold its
            // gate (spinner, commit locked, no status) for as long as the others take – minutes offline (review 0.4.0, A3).
            // Each sets its LastAutoSync when it starts, so the next intervals spread out by themselves.
            await EachAsync(due, MaxParallelNetwork - 1, async (repo, token) =>
            {
                await AutoSyncAsync(repo, token);
                return true;
            }, ct);
        }
        finally
        {
            Interlocked.Decrement(ref _autoSyncRuns);
            AutoSyncRunEnded?.Invoke();
        }
    }

    /// <summary>
    /// Fetch, then – with auto-pull and only if <see cref="PullCheck"/> finds nothing against it – fast-forward. Never a
    /// merge commit or rebase; changed files stay, git refuses if the fast-forward would touch them.
    /// </summary>
    public async Task AutoSyncAsync(RepoState repo, CancellationToken ct)
    {
        if (!await repo.Gate.WaitAsync(0, ct))
        {
            return; // busy: the next tick tries again
        }

        try
        {
            await AutoSyncLockedAsync(repo, ct);
        }
        finally
        {
            repo.Gate.Release();
            Notify();
        }

        await RefreshWorktreesAsync(repo, ct);
    }

    private async Task AutoSyncLockedAsync(RepoState repo, CancellationToken ct)
    {
        repo.LastAutoSync = time.GetUtcNow();
        var behindBefore = repo.Status?.Behind ?? 0;
        var fetched = await RunLockedAsync(repo, SyncText.Op_AutoFetch, network: true,
            c => git.FetchAsync(repo.Path, GitCallMode.Background, c),
            () => repo.Status is { HasUpstream: true } s && s.Behind > behindBefore && !repo.Entry.AutoPull
                ? new ActivityNote(TextFormat.Format(SyncText.NewOnUpstream, Commits(s.Behind), s.Upstream), ActivityKind.NewCommits)
                : null,
            auto: true, ct);
        if (!fetched)
        {
            return; // the reason a pull was skipped stays: a flaky network must not log it again after every fetch (A2)
        }

        if (!repo.Entry.AutoPull || repo.Status is not { Behind: > 0 } status)
        {
            repo.AutoPullBlocked = null;
            repo.LastAutoError = null;
            return;
        }

        var blocker = PullCheck.Blocker(status, repo.Operation);
        if (blocker is not null && blocker != repo.AutoPullBlocked)
        {
            Log(repo.Name, TextFormat.Format(SyncText.AutoPullSkipped, blocker), ActivityKind.Skipped, auto: true, repo.Path);
        }

        repo.AutoPullBlocked = blocker;
        if (blocker is not null)
        {
            repo.LastAutoError = null;
            return;
        }

        var before = status.Head;
        ActivityNote? pulled = null;
        if (await RunLockedAsync(repo, SyncText.Op_AutoPull, network: true, async c =>
            {
                var result = await git.FastForwardAsync(repo.Path, GitCallMode.Background, c);
                pulled = result.Success ? await PulledTextAsync(repo.Path, before, c) : null;
                return result;
            }, () => pulled, auto: true, ct))
        {
            repo.LastAutoError = null;
        }
    }

    // ---------- worktrees (WP-04, ADR 0010) ----------

    /// <summary>
    /// What removing <paramref name="worktree"/> would mean, read now: whether it may go, and the ignored files git would
    /// delete with it. For the question before a single removal; only reads.
    /// </summary>
    public Task<WorktreeCandidate> InspectWorktreeAsync(RepoState worktree, CancellationToken ct) =>
        Task.Run(async () => await InspectAsync(worktree, null, await StatusOfAsync(worktree, ct), ct), ct);

    /// <summary>
    /// The worktrees of <paramref name="repo"/> that look done (decision of the user): branch merged into the remote's
    /// default branch, upstream gone, detached on a commit the default branch contains, folder gone. A worktree on the
    /// default branch's very commit, or on a branch never pushed, is not merged but new – a session that just started
    /// (proposal, not confirmed). Only reads.
    /// </summary>
    public Task<WorktreeCleanup> FindCleanupAsync(RepoState repo, CancellationToken ct) => Task.Run(async () =>
    {
        var target = await git.DefaultBranchAsync(repo.Path, ct);
        var targetCommit = target is null ? null : await git.CommitOfAsync(repo.Path, target, ct);
        var targetName = target?[(target.IndexOf('/', StringComparison.Ordinal) + 1)..]; // origin/main -> main
        var branches = (await git.BranchesAsync(repo.Path, null, ct)).Where(b => !b.IsRemote).ToDictionary(b => b.Name, StringComparer.Ordinal);
        var candidates = new List<WorktreeCandidate>();
        foreach (var worktree in repo.Worktrees)
        {
            var status = await StatusOfAsync(worktree, ct);
            var branch = status is not null ? status.Branch : worktree.Worktree?.Branch;
            var head = status is not null ? status.Head : worktree.Worktree?.Head;
            var info = branch is null ? null : branches.GetValueOrDefault(branch);
            WorktreeReason? reason = null;
            if (!Directory.Exists(worktree.Path))
            {
                reason = WorktreeReason.FolderMissing;
            }
            else if (info is { Gone: true })
            {
                reason = WorktreeReason.UpstreamGone;
            }
            // A branch never pushed whose commit the default branch contains has nothing of its own yet – a session that
            // started before the default branch moved on – rather than being merged. The default branch's own worktree
            // ("main" behind origin/main) is never done (review WP-04).
            else if (targetCommit is not null && head is not null && head != targetCommit
                && (branch is null || (info?.Upstream is { } upstream && upstream != target && branch != targetName))
                && await git.IsAncestorAsync(repo.Path, head, targetCommit, ct) == true)
            {
                reason = branch is null ? WorktreeReason.DetachedMerged : WorktreeReason.Merged;
            }

            if (reason is not null)
            {
                candidates.Add(await InspectAsync(worktree, reason, status, ct));
            }
        }

        return new WorktreeCleanup(target, candidates);
    }, ct);

    private async Task<RepoStatus?> StatusOfAsync(RepoState worktree, CancellationToken ct) =>
        Directory.Exists(worktree.Path) ? (await git.StatusAsync(worktree.Path, ct)).Status : null;

    private async Task<WorktreeCandidate> InspectAsync(RepoState worktree, WorktreeReason? reason, RepoStatus? status, CancellationToken ct)
    {
        if (await BlockAsync(worktree, status, ct) is { } block)
        {
            return new(worktree, reason, block, []);
        }

        if (!Directory.Exists(worktree.Path))
        {
            return new(worktree, reason, null, []);
        }

        // Without the list of what git would delete unasked, no removal (ADR 0010).
        return await git.IgnoredAsync(worktree.Path, ct) is { } ignored
            ? new(worktree, reason, null, ignored)
            : new(worktree, reason, WorktreeBlock.Unknown, []);
    }

    /// <summary>
    /// Why <paramref name="worktree"/> must stay right now, or null. Asked when the question opens and again just before git
    /// removes it (review WP-04: a rebase started in between, a detached HEAD's own commits).
    /// </summary>
    private async Task<WorktreeBlock?> BlockAsync(RepoState worktree, RepoStatus? status, CancellationToken ct)
    {
        if (worktree.Worktree?.IsLocked == true)
        {
            return WorktreeBlock.Locked;
        }

        string? detachedHead;
        if (Directory.Exists(worktree.Path))
        {
            if (status is null || await git.GitDirAsync(worktree.Path, ct) is not { } gitDir)
            {
                return WorktreeBlock.Unknown;
            }

            if (!status.IsClean)
            {
                return WorktreeBlock.Changes;
            }

            if (GitClient.OperationIn(gitDir) is not null)
            {
                return WorktreeBlock.Operation;
            }

            detachedHead = status.IsDetached ? status.Head : null;
        }
        else
        {
            detachedHead = worktree.Worktree is { Branch: null, Head: { } head } ? head : null;
        }

        // A detached HEAD may hold commits no branch has; its reflog goes with the worktree, and with it the last trace.
        return detachedHead is not null && await git.CountUnreferencedAsync(worktree.MainRepo!.Path, detachedHead, ct) is not 0
            ? WorktreeBlock.UnreferencedCommits
            : null;
    }

    /// <summary>What stops a removal, as a sentence – also the reason the question shows.</summary>
    public static string BlockText(WorktreeBlock block) => block switch
    {
        WorktreeBlock.Changes => SyncText.Worktree_Block_Changes,
        WorktreeBlock.Locked => SyncText.Worktree_Block_Locked,
        WorktreeBlock.Operation => SyncText.Worktree_Block_Operation,
        WorktreeBlock.UnreferencedCommits => SyncText.Worktree_Block_UnreferencedCommits,
        _ => SyncText.Worktree_Block_Unknown,
    };

    /// <summary>
    /// Removes linked worktrees one after the other – folder and git's data, never the branch, never with <c>--force</c>
    /// (ADR 0010). Each is checked again first: whatever the question showed, one that must stay now stays, and so does one
    /// with an ignored file the user has not seen.
    /// </summary>
    public async Task RemoveWorktreesAsync(IReadOnlyList<WorktreeCandidate> confirmed, CancellationToken ct)
    {
        foreach (var candidate in confirmed)
        {
            await RemoveWorktreeAsync(candidate, ct);
        }
    }

    /// <inheritdoc cref="RemoveWorktreesAsync"/>
    public async Task<bool> RemoveWorktreeAsync(WorktreeCandidate confirmed, CancellationToken ct)
    {
        var worktree = confirmed.Worktree;
        if (worktree.MainRepo is not { } main)
        {
            throw new ArgumentException("Only a linked worktree can be removed.", nameof(confirmed));
        }

        var branch = worktree.Status?.Branch ?? worktree.Worktree?.Branch;
        var removed = await RunAsync(worktree, SyncText.Op_RemoveWorktree, network: false, async c =>
        {
            if (await BlockAsync(worktree, await StatusOfAsync(worktree, c), c) is { } block)
            {
                return GitResult.Failed(BlockText(block));
            }

            if (Directory.Exists(worktree.Path)
                && (await git.IgnoredAsync(worktree.Path, c) is not { } ignored || ignored.Except(confirmed.Ignored, StringComparer.Ordinal).Any()))
            {
                return GitResult.Failed(SyncText.Worktree_IgnoredChanged);
            }

            return await git.RemoveWorktreeAsync(main.Path, worktree.Path, c);
        }, () => Info(branch is null
            ? TextFormat.Format(SyncText.Worktree_Removed, worktree.Path)
            : TextFormat.Format(SyncText.Worktree_RemovedBranchStays, worktree.Path, branch)), ct);

        if (removed)
        {
            main.RemoveWorktree(worktree);
            Notify();
            await RefreshAsync(main, ct);
        }

        return removed;
    }

    /// <summary>
    /// The main worktree when <paramref name="root"/> is a linked worktree; null otherwise. Only a linked worktree's git
    /// folder holds <c>commondir</c>: for a submodule or a separate git folder, git lists the git folder itself as the main
    /// worktree (review WP-04) – also for a linked worktree of those, so the main worktree must be a work tree itself (second
    /// review pass).
    /// </summary>
    private async Task<string?> MainOfLinkedWorktreeAsync(string root, CancellationToken ct) =>
        await git.GitDirAsync(root, ct) is { } gitDir && File.Exists(Path.Combine(gitDir, "commondir"))
        && await git.WorktreesAsync(root, ct) is [{ IsBare: false } main, ..] && Path.Exists(Path.Combine(main.Path, ".git"))
            ? main.Path
            : null;

    /// <summary>
    /// The linked worktrees of a repository on the board, keeping the state of those it knew. Only for the main worktree: a
    /// linked one added before WP-04 shows none.
    /// </summary>
    private async Task ReadWorktreesAsync(RepoState repo, CancellationToken ct)
    {
        var known = repo.Worktrees;
        if (await git.WorktreesAsync(repo.Path, ct) is not { } list)
        {
            return; // keep the last list
        }

        if (list is not [{ IsBare: false } main, ..] || !SamePath(main.Path, repo.Path))
        {
            repo.ReplaceWorktrees(known, []);
            return;
        }

        // A worktree on a network share of another host only for a repository on a share itself: every status there would
        // send a sign-in to that host, and the entry came from a file in the repository, not from the user (review 0.4.0, A9).
        ImmutableList<RepoState> next = [.. list.Skip(1).Where(w => !w.IsBare && (!Paths.IsShare(w.Path) || Paths.IsShare(repo.Path))).Select(info =>
        {
            var worktree = known.FirstOrDefault(w => SamePath(w.Path, info.Path)) ?? new RepoState(new RepoEntry(info.Path), repo);
            worktree.Worktree = info;
            return worktree;
        })];

        // The same worktrees keep the same list: the file watchers rebuild their map only when it changed (review WP-03).
        if (!next.SequenceEqual(known))
        {
            repo.ReplaceWorktrees(known, next);
        }
    }

    /// <summary>A worktree root: <c>.git</c> as a folder, or as a file in a linked worktree or submodule.</summary>
    private static bool IsRepository(string path) => Path.Exists(Path.Combine(path, ".git"));

    private static bool SamePath(string a, string b) => Paths.Same(a, b);


    // ---------- plumbing ----------

    private async Task<bool> RunAsync(RepoState repo, string label, bool network, Func<CancellationToken, Task<GitResult>> operation,
        Func<ActivityNote?>? success, CancellationToken ct)
    {
        await repo.Gate.WaitAsync(ct);
        bool succeeded;
        try
        {
            succeeded = await RunLockedAsync(repo, label, network, operation, success, auto: false, ct);
        }
        finally
        {
            repo.Gate.Release();
            Notify();
        }

        // The refs are shared: after a fetch, pull or push in a repository or one of its worktrees, all of them changed.
        if (network)
        {
            if (repo.MainRepo is { } main)
            {
                await RefreshAsync(main, ct);
            }

            await RefreshWorktreesAsync(repo.MainRepo ?? repo, ct, except: repo);
        }

        return succeeded;
    }

    private async Task<bool> RunLockedAsync(RepoState repo, string label, bool network, Func<CancellationToken, Task<GitResult>> operation,
        Func<ActivityNote?>? success, bool auto, CancellationToken ct)
    {
        repo.Busy = label;
        Notify();
        GitResult result;
        try
        {
            if (Directory.Exists(repo.Path) && !IsRepository(repo.Path))
            {
                // Without its .git git would find the repository around the folder and work there (review 0.4.0, A6). A
                // folder that is gone altogether is git's to report – removing a worktree whose folder is gone runs in its
                // repository.
                result = GitResult.Failed(TextFormat.Format(SyncText.NotARepository, repo.Path));
            }
            else
            {
                if (network)
                {
                    // Always the family first, then a slot of the network: one order, no two waiting for each other.
                    await repo.FamilyNetwork.WaitAsync(ct);
                    try
                    {
                        await _network.WaitAsync(ct);
                    }
                    catch
                    {
                        repo.FamilyNetwork.Release();
                        throw;
                    }
                }

                try
                {
                    result = await operation(ct);
                }
                finally
                {
                    if (network)
                    {
                        _network.Release();
                        repo.FamilyNetwork.Release();
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = GitResult.Failed(ex.Message);
        }

        var error = result.Success ? null : TextFormat.Format(SyncText.OperationFailed, label, result.Message);
        // The same failure on every auto-sync (offline, sign-in needed, a pull local changes keep out) is logged once, not
        // every few minutes – compared with the last one auto-sync logged, which a successful fetch in between does not
        // clear (review 0.4.0, A2).
        var repeated = auto && error is not null && error == repo.LastAutoError;
        if (auto && error is not null)
        {
            repo.LastAutoError = error;
        }

        repo.Error = error;
        try
        {
            await RefreshCoreAsync(repo, ct);
        }
        finally
        {
            repo.Busy = null;
        }

        var note = error is not null ? new ActivityNote(error, ActivityKind.Error) : success?.Invoke();
        if (note is { } n && !repeated)
        {
            Log(repo.Name, n.Text, n.Kind, auto, repo.Path);
        }

        return result.Success;
    }

    private async Task<ActivityNote> PulledTextAsync(string repo, string? before, CancellationToken ct)
    {
        var (status, _) = await git.StatusAsync(repo, ct);
        if (status?.Head is not { } after || after == before)
        {
            return new(SyncText.AlreadyUpToDate, ActivityKind.Info);
        }

        var count = before is null ? null : await git.CountAsync(repo, $"{before}..{after}", ct);
        return new(count is { } n
            ? TextFormat.Format(SyncText.Pulled, Commits(n), status.Branch)
            : TextFormat.Format(SyncText.Updated, status.Branch), ActivityKind.Pulled);
    }

    private void Log(string repo, string text, ActivityKind kind, bool auto = false, string? path = null) =>
        Activity.Add(new ActivityEntry(time.GetLocalNow(), repo, text, kind, auto, path));

    private static ActivityNote? Info(string text) => new ActivityNote(text, ActivityKind.Info);

    private void Notify() => Changed?.Invoke();

    private static string Commits(int n) => TextFormat.Plural(n, SyncText.CommitsOne, SyncText.CommitsOther);

    /// <summary>What a successful operation tells the activity log.</summary>
    private readonly record struct ActivityNote(string Text, ActivityKind Kind);
}
