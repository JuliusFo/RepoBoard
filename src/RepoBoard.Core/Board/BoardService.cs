using System.Collections.Immutable;
using RepoBoard.Core.Git;
using RepoBoard.Core.Repos;
using RepoBoard.Core.Resources;
using RepoBoard.Core.Sync;

namespace RepoBoard.Core.Board;

/// <summary>
/// The board: the repositories, their state and every git operation on them. One operation per repository at a time (its
/// gate), at most <see cref="MaxParallelNetwork"/> network operations at once. <see cref="Changed"/> fires from any
/// thread; the UI marshals itself.
/// </summary>
public sealed class BoardService(GitClient git, ConfigStore store, TimeProvider time)
{
    public const int MaxParallelNetwork = 4;
    private const int LogLength = 15;

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
            var root = await git.FindRootAsync(path, ct);
            if (root is null)
            {
                Log(Path.GetFileName(path), TextFormat.Format(SyncText.NotARepository, path), ActivityKind.Error);
                continue;
            }

            var repo = new RepoState(new RepoEntry(root));
            var exists = false;
            ImmutableInterlocked.Update(ref _repos, list =>
            {
                exists = list.Any(r => string.Equals(r.Path, root, StringComparison.OrdinalIgnoreCase));
                return exists ? list : [.. list.Add(repo).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];
            });
            if (!exists)
            {
                added.Add(repo);
            }
        }

        if (added.Count > 0)
        {
            await SaveAsync(ct);
            Notify();
            await Task.WhenAll(added.Select(r => RefreshAsync(r, ct)));
        }

        return added.Count;
    }

    /// <summary>Adds every repository below <paramref name="root"/> and logs how many were new.</summary>
    public async Task<int> ScanAsync(string root, CancellationToken ct)
    {
        var found = await Task.Run(() => RepoScanner.Find(root), ct);
        var added = await AddAsync(found, ct);
        Log(Path.GetFileName(root), added == 0 ? SyncText.Scan_NothingNew : TextFormat.Plural(added, SyncText.Scan_AddedOne, SyncText.Scan_AddedOther), ActivityKind.Info);
        Notify();
        return added;
    }

    public async Task RemoveAsync(RepoState repo, CancellationToken ct)
    {
        ImmutableInterlocked.Update(ref _repos, list => list.Remove(repo));
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
            Notify();
        }
    }

    public Task RefreshAllAsync(CancellationToken ct) =>
        Parallel.ForEachAsync(_repos, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
            async (repo, token) => await RefreshAsync(repo, token));

    /// <summary>Reads the status again; skipped while an operation runs on the repository (it refreshes when done).</summary>
    public async Task RefreshAsync(RepoState repo, CancellationToken ct)
    {
        if (!await repo.Gate.WaitAsync(0, ct))
        {
            return;
        }

        try
        {
            await RefreshCoreAsync(repo, ct);
        }
        finally
        {
            repo.Gate.Release();
            Notify();
        }
    }

    private async Task RefreshCoreAsync(RepoState repo, CancellationToken ct)
    {
        if (!Directory.Exists(repo.Path))
        {
            repo.Missing = true;
            repo.Status = null;
            return;
        }

        repo.Missing = false;
        repo.GitDir ??= await git.GitDirAsync(repo.Path, ct);
        var (status, result) = await git.StatusAsync(repo.Path, ct);
        if (status is null)
        {
            repo.Error = TextFormat.Format(SyncText.OperationFailed, SyncText.Op_Status, result.Message);
            return;
        }

        repo.Status = status;
        if (repo.GitDir is { } dir)
        {
            repo.LastFetch = GitClient.LastFetchIn(dir);
            repo.Operation = GitClient.OperationIn(dir);
        }

        if (repo.WantsDetails)
        {
            var branches = await git.BranchesAsync(repo.Path, status.Branch, ct);
            var log = status.IsInitial ? [] : await git.LogAsync(repo.Path, LogLength, ct);
            repo.Details = new RepoDetails(branches, log);
        }
    }

    /// <summary>
    /// The diff of one changed file for the opened row. Only reads: it does not wait for the repository's gate and leaves
    /// <see cref="RepoState.Error"/> alone – a failure is shown in place of the diff. Runs on the thread pool: the UI calls it
    /// from the renderer's context, where reading and parsing a large diff would stall the window (review WP-05).
    /// </summary>
    public async Task<(FileDiff? Diff, string? Error)> DiffAsync(RepoState repo, FileChange change, bool staged, CancellationToken ct)
    {
        try
        {
            var (diff, result) = await Task.Run(() => git.DiffAsync(repo.Path, change, staged, ct), ct);
            return diff is not null ? (diff, null) : (null, TextFormat.Format(SyncText.OperationFailed, SyncText.Op_Diff, result.Message));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, TextFormat.Format(SyncText.OperationFailed, SyncText.Op_Diff, ex.Message));
        }
    }

    // ---------- operations (clicks) ----------

    /// <summary>"Fetch all" – the button on the page and the tray's menu.</summary>
    public Task FetchAllAsync(CancellationToken ct) => Task.WhenAll(_repos.Select(r => FetchAsync(r, ct)));

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
            var result = await git.PullAsync(repo.Path, GitCallMode.Interactive, c);
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

    /// <summary>Unstages <paramref name="paths"/>; all when empty.</summary>
    public Task<bool> UnstageAsync(RepoState repo, IReadOnlyList<string> paths, CancellationToken ct) =>
        RunAsync(repo, SyncText.Op_Unstage, network: false,
            c => git.UnstageAsync(repo.Path, paths.Count == 0 ? ["."] : paths, repo.Status?.IsInitial == true, c), null, ct);

    /// <summary>Commits what is staged; with nothing staged, everything (like "commit all" elsewhere).</summary>
    public Task<bool> CommitAsync(RepoState repo, string message, bool push, CancellationToken ct)
    {
        var stageAll = repo.Status is { Staged: 0 };
        var setUpstream = repo.Status is { HasUpstream: false, IsDetached: false };
        return RunAsync(repo, push ? SyncText.Op_CommitPush : SyncText.Op_Commit, network: push, async c =>
        {
            if (stageAll && await git.StageAllAsync(repo.Path, c) is { Success: false } staged)
            {
                return staged;
            }

            var committed = await git.CommitAsync(repo.Path, message, c);
            return !committed.Success || !push ? committed : await git.PushAsync(repo.Path, setUpstream, c);
        }, () => Info(TextFormat.Format(push ? SyncText.CommittedAndPushed : SyncText.Committed, FirstLine(message))), ct);
    }

    public Task<bool> SwitchAsync(RepoState repo, BranchInfo branch, CancellationToken ct)
    {
        // A remote branch "origin/feature" is switched to as "feature": git creates the local tracking branch.
        var name = branch.IsRemote ? branch.Name[(branch.Name.IndexOf('/', StringComparison.Ordinal) + 1)..] : branch.Name;
        return RunAsync(repo, SyncText.Op_Switch, network: false, c => CheckedAsync(repo, name, git.SwitchAsync, c),
            () => Info(TextFormat.Format(SyncText.Switched, name)), ct);
    }

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
            await Task.WhenAll(due.Select(r => AutoSyncAsync(r, ct)));
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
            repo.LastAutoSync = time.GetUtcNow();
            var behindBefore = repo.Status?.Behind ?? 0;
            var fetched = await RunLockedAsync(repo, SyncText.Op_AutoFetch, network: true,
                c => git.FetchAsync(repo.Path, GitCallMode.Background, c),
                () => repo.Status is { HasUpstream: true } s && s.Behind > behindBefore && !repo.Entry.AutoPull
                    ? new ActivityNote(TextFormat.Format(SyncText.NewOnUpstream, Commits(s.Behind), s.Upstream), ActivityKind.NewCommits)
                    : null,
                auto: true, ct);
            if (!fetched || !repo.Entry.AutoPull || repo.Status is not { Behind: > 0 } status)
            {
                repo.AutoPullBlocked = null;
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
                return;
            }

            var before = status.Head;
            ActivityNote? pulled = null;
            await RunLockedAsync(repo, SyncText.Op_AutoPull, network: false, async c =>
            {
                var result = await git.FastForwardAsync(repo.Path, c);
                pulled = result.Success ? await PulledTextAsync(repo.Path, before, c) : null;
                return result;
            }, () => pulled, auto: true, ct);
        }
        finally
        {
            repo.Gate.Release();
            Notify();
        }
    }

    // ---------- plumbing ----------

    private async Task<bool> RunAsync(RepoState repo, string label, bool network, Func<CancellationToken, Task<GitResult>> operation,
        Func<ActivityNote?>? success, CancellationToken ct)
    {
        await repo.Gate.WaitAsync(ct);
        try
        {
            return await RunLockedAsync(repo, label, network, operation, success, auto: false, ct);
        }
        finally
        {
            repo.Gate.Release();
            Notify();
        }
    }

    private async Task<bool> RunLockedAsync(RepoState repo, string label, bool network, Func<CancellationToken, Task<GitResult>> operation,
        Func<ActivityNote?>? success, bool auto, CancellationToken ct)
    {
        repo.Busy = label;
        Notify();
        GitResult result;
        try
        {
            if (network)
            {
                await _network.WaitAsync(ct);
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
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = GitResult.Failed(ex.Message);
        }

        var error = result.Success ? null : TextFormat.Format(SyncText.OperationFailed, label, result.Message);
        // The same failure on every auto-sync (offline, sign-in needed) is logged once, not every few minutes.
        var repeated = auto && error is not null && error == repo.Error;
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

    private static string FirstLine(string text) => text.Split('\n', 2)[0].Trim();

    /// <summary>What a successful operation tells the activity log.</summary>
    private readonly record struct ActivityNote(string Text, ActivityKind Kind);
}
