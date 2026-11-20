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

    public Task<GitResult> StageAsync(string repo, IEnumerable<string> paths, CancellationToken ct) =>
        Local(repo, ct, ["add", "-A", "--", .. paths]);

    public Task<GitResult> StageAllAsync(string repo, CancellationToken ct) => Local(repo, ct, "add", "-A");

    /// <summary>Takes files out of the index; before the first commit there is no HEAD to restore from.</summary>
    public Task<GitResult> UnstageAsync(string repo, IEnumerable<string> paths, bool initial, CancellationToken ct) =>
        initial
            ? Local(repo, ct, ["rm", "--cached", "-r", "-q", "--", .. paths])
            : Local(repo, ct, ["restore", "--staged", "--", .. paths]);

    public Task<GitResult> CommitAsync(string repo, string message, CancellationToken ct) =>
        Local(repo, ct, "commit", "-m", message);

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
}
