namespace RepoBoard.Core.Git;

/// <summary>A merge, rebase, cherry-pick or revert left unfinished in a repository (read from its git folder).</summary>
public enum GitOperation
{
    Merge,
    Rebase,
    CherryPick,
    Revert,
}

/// <summary>
/// The git commands RepoBoard uses, each one CLI call through <see cref="GitRunner"/>. Every command text lives here
/// (ADR 0002). What comes from outside – paths, branch names – goes after <c>--</c> or is checked first
/// (<see cref="IsBranchNameAsync"/>), so a value can never be read as an option.
/// </summary>
public sealed class GitClient(GitRunner runner)
{
    private Task<GitResult> Local(string repo, CancellationToken ct, params string[] args) =>
        runner.RunAsync(repo, args, GitCallMode.Interactive, GitRunner.LocalTimeout, ct);

    private Task<GitResult> Network(string repo, GitCallMode mode, CancellationToken ct, params string[] args) =>
        runner.RunAsync(repo, args, mode, GitRunner.NetworkTimeout, ct);

    /// <summary>The worktree root of <paramref name="path"/>, or null if it is not inside a repository.</summary>
    public async Task<string?> FindRootAsync(string path, CancellationToken ct)
    {
        if (!Directory.Exists(path))
        {
            return null;
        }

        var result = await Local(path, ct, "rev-parse", "--show-toplevel");
        return result.Success ? Path.GetFullPath(result.Output.Trim()) : null;
    }

    public async Task<string?> GitDirAsync(string repo, CancellationToken ct)
    {
        var result = await Local(repo, ct, "rev-parse", "--absolute-git-dir");
        return result.Success ? Path.GetFullPath(result.Output.Trim()) : null;
    }

    public async Task<(RepoStatus? Status, GitResult Result)> StatusAsync(string repo, CancellationToken ct)
    {
        var result = await Local(repo, ct, "status", "--porcelain=v2", "--branch", "-z", "--untracked-files=all");
        return (result.Success ? RepoStatus.Parse(result.Output) : null, result);
    }

    /// <summary>The unfinished operation in <paramref name="gitDir"/>, if any (marker files of git).</summary>
    public static GitOperation? OperationIn(string gitDir) =>
        File.Exists(Path.Combine(gitDir, "MERGE_HEAD")) ? GitOperation.Merge
        : Directory.Exists(Path.Combine(gitDir, "rebase-merge")) || Directory.Exists(Path.Combine(gitDir, "rebase-apply")) ? GitOperation.Rebase
        : File.Exists(Path.Combine(gitDir, "CHERRY_PICK_HEAD")) ? GitOperation.CherryPick
        : File.Exists(Path.Combine(gitDir, "REVERT_HEAD")) ? GitOperation.Revert
        : null;

    /// <summary>The time of the last fetch – from FETCH_HEAD, so a fetch in the IDE or terminal counts too.</summary>
    public static DateTimeOffset? LastFetchIn(string gitDir)
    {
        var fetchHead = new FileInfo(Path.Combine(gitDir, "FETCH_HEAD"));
        return fetchHead.Exists ? new DateTimeOffset(fetchHead.LastWriteTimeUtc, TimeSpan.Zero) : null;
    }

    public Task<GitResult> FetchAsync(string repo, GitCallMode mode, CancellationToken ct) =>
        Network(repo, mode, ct, "fetch", "--all", "--prune");

    /// <summary>Only fast-forward: never a merge commit, never a rebase, nothing to resolve (ADR 0003).</summary>
    public Task<GitResult> PullAsync(string repo, GitCallMode mode, CancellationToken ct) =>
        Network(repo, mode, ct, "pull", "--ff-only");

    /// <summary>After a fetch: fast-forward to the upstream without a second network round trip.</summary>
    public Task<GitResult> FastForwardAsync(string repo, CancellationToken ct) =>
        Local(repo, ct, "merge", "--ff-only", "@{u}");

    /// <summary>Push; the first push of a branch sets its upstream on <c>origin</c>.</summary>
    public Task<GitResult> PushAsync(string repo, bool setUpstream, CancellationToken ct) =>
        setUpstream
            ? Network(repo, GitCallMode.Interactive, ct, "push", "-u", "origin", "HEAD")
            : Network(repo, GitCallMode.Interactive, ct, "push");

    /// <summary>
    /// Before a command that takes paths from the status: they are file names, not patterns – "[id].tsx" must not also match
    /// "i.tsx" (review WP-05). Only on these commands, which start no hooks: git exports it to its children, and a hook
    /// with a glob ("*.cs") would find nothing (second review pass WP-05).
    /// </summary>
    private const string LiteralPaths = "--literal-pathspecs";

    public Task<GitResult> StageAsync(string repo, IEnumerable<string> paths, CancellationToken ct) =>
        Local(repo, ct, [LiteralPaths, "add", "-A", "--", .. paths]);

    public Task<GitResult> StageAllAsync(string repo, CancellationToken ct) => Local(repo, ct, "add", "-A");

    /// <summary>Takes files out of the index; before the first commit there is no HEAD to restore from.</summary>
    public Task<GitResult> UnstageAsync(string repo, IEnumerable<string> paths, bool initial, CancellationToken ct) =>
        initial
            ? Local(repo, ct, [LiteralPaths, "rm", "--cached", "-r", "-q", "--", .. paths])
            : Local(repo, ct, [LiteralPaths, "restore", "--staged", "--", .. paths]);

    public Task<GitResult> CommitAsync(string repo, string message, CancellationToken ct) =>
        Local(repo, ct, "commit", "-m", message);

    /// <summary>
    /// The diff of one changed file (ADR 0008): staged against HEAD (a rename with its original path), unstaged against the
    /// index, a new file against nothing (<c>--no-index</c>, which ends with 1 when the files differ). Never an external diff
    /// tool; textconv and the user's diff settings apply as in the terminal. Conflicted files and nested repositories are the
    /// caller's to leave out. A file above <see cref="FileDiff.MaxFileBytes"/> in the worktree is not read (git would read it
    /// whole, with every new status while the diff is open; review WP-05).
    /// </summary>
    public async Task<(FileDiff? Diff, GitResult Result)> DiffAsync(string repo, FileChange change, bool staged, CancellationToken ct)
    {
        var file = new FileInfo(Path.Combine(repo, change.Path));
        if (file.Exists && file.Length > FileDiff.MaxFileBytes)
        {
            return (FileDiff.TooLarge, new GitResult(0, "", ""));
        }

        string[] options = [LiteralPaths, "diff", "--no-color", "--no-ext-diff"];
        GitResult result;
        if (change.Untracked)
        {
            result = await Local(repo, ct, [.. options, "--no-index", "--", "/dev/null", change.Path]);
            // 1 = the files differ – and also an error ("Could not access …"). stderr may hold a mere warning ("LF will be
            // replaced by CRLF" with core.autocrlf, E2E WP-05), and git's messages may be translated: a diff on stdout is
            // what makes it a success (review WP-05), never the wording.
            if (result.ExitCode == 1 && result.Output.Length > 0)
            {
                result = result with { ExitCode = 0 };
            }
        }
        else if (staged)
        {
            result = await Local(repo, ct, [.. options, "--cached", "-M", "--", .. Paths(change)]);
        }
        else
        {
            result = await Local(repo, ct, [.. options, "--", change.Path]);
        }

        return (result.Success ? FileDiff.Parse(result.Output) : null, result);

        static string[] Paths(FileChange c) => c.OriginalPath is { } from ? [from, c.Path] : [c.Path];
    }

    /// <summary>
    /// True if git accepts <paramref name="name"/> as a branch name. A name starting with <c>-</c> is refused before git
    /// sees it: in <c>git switch</c> it would be an option.
    /// </summary>
    public async Task<bool> IsBranchNameAsync(string repo, string name, CancellationToken ct) =>
        name.Length > 0 && !name.StartsWith('-') && (await Local(repo, ct, "check-ref-format", "--branch", name)).Success;

    /// <summary>
    /// A local branch, or a remote one: <c>git switch</c> creates the local tracking branch for a unique remote branch.
    /// The caller checks the name (<see cref="IsBranchNameAsync"/>).
    /// </summary>
    public Task<GitResult> SwitchAsync(string repo, string branch, CancellationToken ct) =>
        Local(repo, ct, "switch", branch);

    /// <inheritdoc cref="SwitchAsync"/>
    public Task<GitResult> CreateBranchAsync(string repo, string branch, CancellationToken ct) =>
        Local(repo, ct, "switch", "-c", branch);

    public async Task<IReadOnlyList<BranchInfo>> BranchesAsync(string repo, string? current, CancellationToken ct)
    {
        var result = await Local(repo, ct, "for-each-ref",
            "--format=%(refname)%09%(upstream:short)%09%(upstream:track,nobracket)%09%(committerdate:relative)",
            "refs/heads", "refs/remotes");
        return result.Success ? ParseBranches(result.Output, current) : [];
    }

    internal static IReadOnlyList<BranchInfo> ParseBranches(string output, string? current)
    {
        var local = new List<BranchInfo>();
        var remote = new List<BranchInfo>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.TrimEnd('\r').Split('\t');
            var name = parts[0];
            string? Part(int i) => parts.Length > i && parts[i].Length > 0 ? parts[i] : null;
            if (name.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                var shortName = name["refs/heads/".Length..];
                local.Add(new BranchInfo(shortName, shortName == current, false, Part(1), Part(2), Part(3)));
            }
            else if (name.StartsWith("refs/remotes/", StringComparison.Ordinal) && !name.EndsWith("/HEAD", StringComparison.Ordinal))
            {
                var shortName = name["refs/remotes/".Length..];
                remote.Add(new BranchInfo(shortName, false, true, null, null, Part(3)));
            }
        }

        // Remote branches already checked out locally (as their upstream) add nothing.
        var tracked = local.Select(b => b.Upstream).OfType<string>().ToHashSet(StringComparer.Ordinal);
        return [.. local.OrderByDescending(b => b.IsCurrent).ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase),
            .. remote.Where(b => !tracked.Contains(b.Name)).OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<int?> CountAsync(string repo, string range, CancellationToken ct)
    {
        var result = await Local(repo, ct, "rev-list", "--count", range);
        return result.Success && int.TryParse(result.Output.Trim(), out var n) ? n : null;
    }

    public async Task<IReadOnlyList<CommitInfo>> LogAsync(string repo, int count, CancellationToken ct)
    {
        var result = await Local(repo, ct, "log", $"-n{count}", "--format=%h%x1f%an%x1f%cr%x1f%D%x1f%s%x1e");
        return result.Success ? ParseLog(result.Output) : [];
    }

    internal static IReadOnlyList<CommitInfo> ParseLog(string output) =>
    [
        .. output.Split('\x1e', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(r => r.Split('\x1f'))
            .Where(p => p.Length >= 5)
            .Select(p => new CommitInfo(p[0], p[1], p[2], p[3], p[4])),
    ];

    // ---------- worktrees (WP-04, ADR 0010) ----------

    /// <summary>The worktrees of the repository, the main one first; null if git fails.</summary>
    public async Task<IReadOnlyList<WorktreeInfo>?> WorktreesAsync(string repo, CancellationToken ct)
    {
        var result = await Local(repo, ct, "worktree", "list", "--porcelain", "-z");
        return result.Success ? WorktreeInfo.ParseList(result.Output) : null;
    }

    /// <summary>The remote's default branch as <c>origin/main</c>, from <c>origin/HEAD</c> (set by a clone); null without one.</summary>
    public async Task<string?> DefaultBranchAsync(string repo, CancellationToken ct)
    {
        var result = await Local(repo, ct, "symbolic-ref", "--quiet", "--short", "refs/remotes/origin/HEAD");
        return result.Success && result.Output.Trim() is { Length: > 0 } name ? name : null;
    }

    /// <summary>The commit a ref or commit id from git names; null if there is none.</summary>
    public async Task<string?> CommitOfAsync(string repo, string rev, CancellationToken ct)
    {
        if (rev.StartsWith('-'))
        {
            return null;
        }

        var result = await Local(repo, ct, "rev-parse", "--verify", "--quiet", rev + "^{commit}");
        return result.Success ? result.Output.Trim() : null;
    }

    /// <summary>True if <paramref name="commit"/> is contained in <paramref name="target"/>; null if git cannot tell.</summary>
    public async Task<bool?> IsAncestorAsync(string repo, string commit, string target, CancellationToken ct)
    {
        if (commit.StartsWith('-') || target.StartsWith('-'))
        {
            return null;
        }

        var result = await Local(repo, ct, "merge-base", "--is-ancestor", commit, target);
        return result.ExitCode switch { 0 => true, 1 => false, _ => null };
    }

    /// <summary>
    /// How many commits of <paramref name="commit"/> no branch, remote branch or tag contains – the work of a detached HEAD
    /// that only its reflog keeps (review WP-04). Null if git cannot tell.
    /// </summary>
    public async Task<int?> CountUnreferencedAsync(string repo, string commit, CancellationToken ct)
    {
        if (commit.StartsWith('-'))
        {
            return null;
        }

        var result = await Local(repo, ct, "rev-list", "--count", commit, "--not", "--branches", "--remotes", "--tags");
        return result.Success && int.TryParse(result.Output.Trim(), out var n) ? n : null;
    }

    /// <summary>
    /// The ignored files and folders of a worktree (a folder as a whole, <c>bin/</c>): <c>git worktree remove</c> deletes
    /// them without asking, so the question before it names them (ADR 0010). Null if git fails.
    /// </summary>
    public async Task<IReadOnlyList<string>?> IgnoredAsync(string worktree, CancellationToken ct)
    {
        var result = await Local(worktree, ct, "status", "--porcelain=v2", "-z", "--ignored=traditional", "--untracked-files=normal");
        return result.Success
            ? [.. result.Output.Split('\0').Where(f => f.StartsWith("! ", StringComparison.Ordinal)).Select(f => f[2..])]
            : null;
    }

    /// <summary>
    /// Removes a linked worktree's folder and git's data about it; its branch stays. Never <c>--force</c> (ADR 0010): git
    /// refuses a worktree with changed or new files and a locked one. Run in the main worktree – a process inside the
    /// folder would keep it from being deleted on Windows.
    /// </summary>
    public Task<GitResult> RemoveWorktreeAsync(string mainWorktree, string worktree, CancellationToken ct) =>
        Local(mainWorktree, ct, "worktree", "remove", "--", worktree);
}
