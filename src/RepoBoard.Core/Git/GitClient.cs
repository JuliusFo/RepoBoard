using System.Globalization;
using RepoBoard.Core.Resources;

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
        runner.RunAsync(repo, args, GitCallMode.Interactive, GitRunner.LocalTimeout, null, ct);

    private Task<GitResult> Network(string repo, GitCallMode mode, CancellationToken ct, params string[] args) =>
        runner.RunAsync(repo, args, mode, GitRunner.NetworkTimeout, null, ct);

    /// <summary>
    /// The worktree root of <paramref name="path"/>; null if it is not inside a repository. <c>Problem</c> is git's own
    /// message when it refused a repository it found (<c>safe.directory</c> for another owner, a broken config) – "not a
    /// repository" would hide what to do (review 0.4.0, A22).
    /// </summary>
    public async Task<(string? Root, string? Problem)> FindRootAsync(string path, CancellationToken ct)
    {
        if (!Directory.Exists(path))
        {
            return (null, null);
        }

        var result = await Local(path, ct, "rev-parse", "--show-toplevel");
        if (result.Success)
        {
            return (Path.GetFullPath(result.Output.Trim()), null);
        }

        for (var dir = new DirectoryInfo(path); dir is not null; dir = dir.Parent)
        {
            if (Path.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return (null, result.Message);
            }
        }

        return (null, null);
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

    /// <summary>
    /// Only fast-forward: never a merge commit, never a rebase, nothing to resolve (ADR 0003). The user's <c>pull.rebase</c>
    /// and autostash settings are overridden: with autostash git would stash overlapping local changes, fast-forward and put
    /// them back with conflict markers – and end with success (review 0.4.0, A1). Without it git refuses and the file stays.
    /// </summary>
    public Task<GitResult> PullAsync(string repo, CancellationToken ct) =>
        Network(repo, GitCallMode.Interactive, ct, "pull", "--ff-only", "--no-rebase", "--no-autostash");

    /// <summary>
    /// After a fetch: fast-forward to the upstream without a second network round trip, never with the user's
    /// <c>merge.autoStash</c> (see <see cref="PullAsync"/>). Checking out the new commits can be network work too – Git LFS,
    /// a partial clone, a <c>post-merge</c> hook –, so it runs with the network's time limit and, for auto-pull, without
    /// a prompt (review 0.4.0, A4).
    /// </summary>
    public Task<GitResult> FastForwardAsync(string repo, GitCallMode mode, CancellationToken ct) =>
        Network(repo, mode, ct, "merge", "--ff-only", "--no-autostash", "@{u}");

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
        InBatchesAsync(paths, batch => Local(repo, ct, [LiteralPaths, "add", "-A", "--", .. batch]));

    /// <summary>
    /// One call per part of <paramref name="paths"/> (<see cref="Batches"/>), up to the first failure: "Stage all" during a
    /// conflict names every file, more than a Windows command line takes (review WP-07).
    /// </summary>
    private static async Task<GitResult> InBatchesAsync(IEnumerable<string> paths, Func<List<string>, Task<GitResult>> run)
    {
        var result = new GitResult(0, "", "");
        foreach (var batch in Batches([.. paths], 16_000))
        {
            result = await run(batch);
            if (!result.Success)
            {
                break;
            }
        }

        return result;
    }

    public Task<GitResult> StageAllAsync(string repo, CancellationToken ct) => Local(repo, ct, "add", "-A");

    /// <summary>Takes files out of the index; before the first commit there is no HEAD to restore from.</summary>
    public Task<GitResult> UnstageAsync(string repo, IEnumerable<string> paths, bool initial, CancellationToken ct) =>
        InBatchesAsync(paths, batch => initial
            ? Local(repo, ct, [LiteralPaths, "rm", "--cached", "-r", "-q", "--", .. batch])
            : Local(repo, ct, [LiteralPaths, "restore", "--staged", "--", .. batch]));

    /// <summary>
    /// Applies part of a hunk to the index (WP-25, ADR 0012): always <c>--cached</c>, so the worktree is never touched; git
    /// checks the patch against the index and applies all of it or nothing. The user's <c>apply.whitespace</c> must not
    /// refuse a line that is already in the file (<c>--whitespace=nowarn</c>); a hunk without context at one of its ends
    /// (the user's <c>diff.context=0</c>) needs <c>--unidiff-zero</c>.
    /// </summary>
    public Task<GitResult> ApplyToIndexAsync(string repo, HunkPatch patch, bool reverse, CancellationToken ct)
    {
        List<string> args = ["apply", "--cached", "--whitespace=nowarn"];
        if (reverse)
        {
            args.Add("--reverse");
        }

        if (patch.UnidiffZero)
        {
            args.Add("--unidiff-zero");
        }

        args.Add("-");
        return runner.RunAsync(repo, args, GitCallMode.Interactive, GitRunner.LocalTimeout, patch.Text, ct);
    }

    public Task<GitResult> CommitAsync(string repo, string message, CancellationToken ct) =>
        Local(repo, ct, "commit", "-m", message);

    /// <summary>
    /// The diff of one changed file (ADR 0008): staged against HEAD (a rename with its original path), unstaged against the
    /// index, a new file against nothing (<c>--no-index</c>, which ends with 1 when the files differ). Never an external diff
    /// tool; textconv and the user's diff settings apply as in the terminal. Conflicted files and nested repositories are the
    /// caller's to leave out. A file above <see cref="FileDiff.MaxFileBytes"/> in the worktree is not read (git would read it
    /// whole, with every new status while the diff is open; review WP-05).
    /// </summary>
    /// <param name="textconv">False for the file's own text instead of what a diff driver makes of it – the text a patch
    /// for the index must be built from (review WP-25).</param>
    public async Task<(FileDiff? Diff, GitResult Result)> DiffAsync(string repo, FileChange change, bool staged, CancellationToken ct,
        bool textconv = true)
    {
        var file = new FileInfo(Path.Combine(repo, change.Path));
        if (file.Exists && file.Length > FileDiff.MaxFileBytes)
        {
            return (FileDiff.TooLarge, new GitResult(0, "", ""));
        }

        string[] options = textconv
            ? [LiteralPaths, "diff", "--no-color", "--no-ext-diff"]
            : [LiteralPaths, "diff", "--no-color", "--no-ext-diff", "--no-textconv"];
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

    /// <summary>A local branch. The caller checks the name (<see cref="IsBranchNameAsync"/>).</summary>
    public Task<GitResult> SwitchAsync(string repo, string branch, CancellationToken ct) =>
        Local(repo, ct, "switch", "--no-guess", branch);

    /// <summary>
    /// A remote branch (<c>upstream/main</c>): a new local branch <paramref name="local"/> that tracks exactly it; git refuses
    /// if that name is taken. The name is given, not left to git: with a remote named "up/stream" git cut "up/" only. The
    /// caller checks both names (<see cref="IsBranchNameAsync"/>).
    /// </summary>
    public Task<GitResult> SwitchTrackingAsync(string repo, string local, string remoteBranch, CancellationToken ct) =>
        Local(repo, ct, "switch", "-c", local, "--track", remoteBranch);

    /// <summary>The names of the repository's remotes; empty if git fails.</summary>
    public async Task<IReadOnlyList<string>> RemotesAsync(string repo, CancellationToken ct)
    {
        var result = await Local(repo, ct, "remote");
        return result.Success ? result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
    }

    /// <summary>
    /// The local name for a remote branch: what follows the longest remote name it starts with ("up/stream/feat/x" with a
    /// remote "up/stream" → "feat/x"); null if no remote matches.
    /// </summary>
    public static string? LocalNameOf(string remoteBranch, IEnumerable<string> remotes) =>
        remotes.Where(r => remoteBranch.StartsWith(r + "/", StringComparison.Ordinal) && remoteBranch.Length > r.Length + 1)
            .MaxBy(r => r.Length) is { } remote
            ? remoteBranch[(remote.Length + 1)..]
            : null;

    /// <inheritdoc cref="SwitchAsync"/>
    public Task<GitResult> CreateBranchAsync(string repo, string branch, CancellationToken ct) =>
        Local(repo, ct, "switch", "-c", branch);

    public async Task<IReadOnlyList<BranchInfo>> BranchesAsync(string repo, string? current, CancellationToken ct)
    {
        // for-each-ref writes "ahead 1, behind 2" and "gone" untranslated; the date as a number, shown in the UI's language
        // (review 0.4.0, A19).
        var result = await Local(repo, ct, "for-each-ref",
            "--format=%(refname)%09%(upstream:short)%09%(upstream:track,nobracket)%09%(committerdate:unix)",
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
                var (ahead, behind, gone) = Track(Part(2));
                local.Add(new BranchInfo(shortName, shortName == current, false, Part(1), ahead, behind, gone, Date(Part(3))));
            }
            else if (name.StartsWith("refs/remotes/", StringComparison.Ordinal) && !name.EndsWith("/HEAD", StringComparison.Ordinal))
            {
                var shortName = name["refs/remotes/".Length..];
                remote.Add(new BranchInfo(shortName, false, true, null, 0, 0, false, Date(Part(3))));
            }
        }

        // Remote branches already checked out locally (as their upstream) add nothing.
        var tracked = local.Select(b => b.Upstream).OfType<string>().ToHashSet(StringComparer.Ordinal);
        return [.. local.OrderByDescending(b => b.IsCurrent).ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase),
            .. remote.Where(b => !tracked.Contains(b.Name)).OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)];

        // "ahead 1", "behind 2", "ahead 1, behind 2", "gone" – nothing when in step.
        static (int Ahead, int Behind, bool Gone) Track(string? track)
        {
            if (track == "gone")
            {
                return (0, 0, true);
            }

            int ahead = 0, behind = 0;
            foreach (var part in (track ?? "").Split(", ", StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Split(' ') is [var word, var number] && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                {
                    if (word == "ahead")
                    {
                        ahead = n;
                    }
                    else if (word == "behind")
                    {
                        behind = n;
                    }
                }
            }

            return (ahead, behind, false);
        }
    }

    /// <summary>A date git wrote as seconds since 1970 (<c>%ct</c>, <c>:unix</c>); null if it is none.</summary>
    internal static DateTimeOffset? Date(string? seconds) =>
        long.TryParse(seconds, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var s) ? DateTimeOffset.FromUnixTimeSeconds(s) : null;

    /// <summary>
    /// The commits of <paramref name="range"/>; null if git cannot tell. A range from git's output (an upstream's name, which
    /// a hand-edited config sets) never goes to git as an option (second review pass R1).
    /// </summary>
    public async Task<int?> CountAsync(string repo, string range, CancellationToken ct)
    {
        if (range.StartsWith('-'))
        {
            return null;
        }

        var result = await Local(repo, ct, "rev-list", "--count", range, "--");
        return result.Success && int.TryParse(result.Output.Trim(), out var n) ? n : null;
    }

    /// <summary>
    /// The history of HEAD – with <paramref name="all"/> of every local and remote branch too –, children before their
    /// parents (<c>--topo-order</c>, which <see cref="CommitGraph"/> needs; WP-09). One more than <paramref name="count"/> is
    /// read to tell whether there are more. A signature check (<c>log.showSignature</c>) would write into the output.
    /// </summary>
    public async Task<(IReadOnlyList<CommitInfo> Log, bool HasMore)> LogAsync(string repo, int count, bool all, CancellationToken ct)
    {
        // Fields and commits end with NUL (-z): a subject or author may hold any other character, and with %x1f/%x1e a
        // stranger's commit added entries of its own to the history (review 0.4.0, A7). git cuts a message at NUL.
        List<string> args = ["log", $"-n{count + 1}", "-z", "--topo-order", "--no-show-signature", $"--format={LogFormat}"];
        if (all)
        {
            args.AddRange(["HEAD", "--branches", "--remotes"]);
        }

        // A file named like a ref ("HEAD") would make the revision ambiguous.
        args.Add("--");
        var result = await Local(repo, ct, [.. args]);
        if (!result.Success)
        {
            return ([], false);
        }

        var log = ParseLog(result.Output);
        return log.Count > count ? (log.Take(count).ToList(), true) : (log, false);
    }

    private const string LogFormat = "%H%x00%h%x00%P%x00%an%x00%ct%x00%D%x00%s";
    private const int LogFields = 7;

    /// <summary>The output of <see cref="LogFormat"/> with <c>-z</c>: seven NUL-ended fields per commit, nothing else.</summary>
    internal static IReadOnlyList<CommitInfo> ParseLog(string output)
    {
        var fields = output.Split('\0');
        var log = new List<CommitInfo>();
        for (var i = 0; i + LogFields <= fields.Length; i += LogFields)
        {
            var id = fields[i].Trim('\n', '\r');
            if (!IsCommitId(id) || Date(fields[i + 4]) is not { } when)
            {
                break; // out of step: nothing after this can be trusted
            }

            log.Add(new CommitInfo(id, fields[i + 1], fields[i + 2].Split(' ', StringSplitOptions.RemoveEmptyEntries), fields[i + 3], when,
                fields[i + 5], fields[i + 6]));
        }

        return log;
    }

    /// <summary>
    /// A full commit id as the history gives it (<c>%H</c>: 40 hex digits, 64 with SHA-256). Checked before a command takes
    /// it, so it can never be an option or a range (WP-09) – nor a ref: git resolves a short id to a branch of that name
    /// first (review WP-09).
    /// </summary>
    public static bool IsCommitId(string id) =>
        id.Length is 40 or 64 && id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>One commit with its whole message and changed files (WP-09); a merge against its first parent. Null if git fails.</summary>
    public async Task<(CommitDetails? Commit, GitResult Result)> CommitDetailsAsync(string repo, string id, CancellationToken ct)
    {
        if (!IsCommitId(id))
        {
            return (null, GitResult.Failed(TextFormat.Format(SyncText.InvalidCommit, id)));
        }

        var header = await Local(repo, ct, "show", "-s", "--no-show-signature", CommitDetails.Format, id, "--");
        if (!header.Success)
        {
            return (null, header);
        }

        var files = await Local(repo, ct, "show", "--format=", "--name-status", "-z", "-M", "--diff-merges=first-parent",
            "--no-show-signature", id, "--");
        if (!files.Success)
        {
            return (null, files);
        }

        var commit = CommitDetails.Parse(header.Output, files.Output);
        return (commit, commit is null ? GitResult.Failed(TextFormat.Format(SyncText.InvalidCommit, id)) : header);
    }

    /// <summary>
    /// The diff of one file of a commit (WP-09, as ADR 0008): against the first parent, a rename with its original path.
    /// A side above <see cref="FileDiff.MaxFileBytes"/> is not read – git would read it whole.
    /// </summary>
    public async Task<(FileDiff? Diff, GitResult Result)> CommitDiffAsync(string repo, CommitDetails commit, CommitFile file, CancellationToken ct)
    {
        if (!IsCommitId(commit.Id) || commit.Parents.Any(p => !IsCommitId(p)))
        {
            return (null, GitResult.Failed(TextFormat.Format(SyncText.InvalidCommit, commit.Id)));
        }

        if (await LargestSideAsync(repo, commit, file, ct) > FileDiff.MaxFileBytes)
        {
            return (FileDiff.TooLarge, new GitResult(0, "", ""));
        }

        string[] paths = file.OriginalPath is { } from ? [from, file.Path] : [file.Path];
        var result = await Local(repo, ct, [LiteralPaths, "show", "--format=", "--no-color", "--no-ext-diff", "--no-show-signature", "-M",
            "--diff-merges=first-parent", commit.Id, "--", .. paths]);
        return (result.Success ? FileDiff.Parse(result.Output) : null, result);
    }

    /// <summary>The size of the larger side of a file in a commit, from <c>cat-file --batch-check</c>; 0 if git cannot tell.</summary>
    private async Task<long> LargestSideAsync(string repo, CommitDetails commit, CommitFile file, CancellationToken ct)
    {
        List<string> objects = [];
        if (file.Status != 'D')
        {
            objects.Add($"{commit.Id}:{file.Path}");
        }

        if (file.Status != 'A' && commit.Parents.Count > 0)
        {
            objects.Add($"{commit.Parents[0]}:{file.OriginalPath ?? file.Path}");
        }

        // One object per line: a path with a line break cannot be asked for (its diff is read without the check).
        if (objects.Count == 0 || objects.Any(o => o.Contains('\n') || o.Contains('\r')))
        {
            return 0;
        }

        var result = await runner.RunAsync(repo, ["cat-file", "--batch-check=%(objectsize)"], GitCallMode.Interactive,
            GitRunner.LocalTimeout, string.Join('\n', objects) + "\n", ct);
        return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => long.TryParse(l, NumberStyles.None, CultureInfo.InvariantCulture, out var size) ? size : 0)
            .DefaultIfEmpty(0)
            .Max();
    }

    /// <summary>The whole message of a commit (for an amend; WP-09); null if git fails.</summary>
    public async Task<string?> MessageAsync(string repo, string rev, CancellationToken ct)
    {
        if (rev.StartsWith('-'))
        {
            return null;
        }

        var result = await Local(repo, ct, "log", "-1", "--no-show-signature", "--format=%B", rev, "--");
        return result.Success ? result.Output.TrimEnd() : null;
    }

    /// <summary>
    /// True if a remote branch contains <paramref name="rev"/> – it was pushed, an amend would need a force push (WP-09).
    /// Null if git cannot tell. One walk from <paramref name="rev"/> that stops at the remotes – read with every status of an
    /// opened row, where <c>for-each-ref --contains</c> would test each remote ref on its own (review WP-09).
    /// </summary>
    public async Task<bool?> IsOnRemoteAsync(string repo, string rev, CancellationToken ct)
    {
        if (rev.StartsWith('-'))
        {
            return null;
        }

        var result = await Local(repo, ct, "rev-list", "-n1", rev, "--not", "--remotes", "--");
        return result.Success ? result.Output.Trim().Length == 0 : null;
    }

    /// <summary>
    /// Which of <paramref name="paths"/> are untracked and ignored in the worktree. git counts such files as expendable: a
    /// revert, cherry-pick or merge that brings a file there overwrites it without a word (review WP-09). Null if git fails.
    /// </summary>
    public async Task<IReadOnlyList<string>?> IgnoredAmongAsync(string repo, IReadOnlyList<string> paths, CancellationToken ct)
    {
        // In batches: a commit with thousands of files would pass the command line's limit on Windows (32,767 characters).
        var ignored = new List<string>();
        foreach (var batch in Batches(paths, 16_000))
        {
            var result = await Local(repo, ct, [LiteralPaths, "ls-files", "-z", "--others", "--ignored", "--exclude-standard", "--", .. batch]);
            if (!result.Success)
            {
                return null;
            }

            ignored.AddRange(result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries));
        }

        return ignored;
    }

    /// <summary>Consecutive parts of <paramref name="paths"/>, each together at most <paramref name="chars"/> long (at least one path).</summary>
    internal static IEnumerable<List<string>> Batches(IReadOnlyList<string> paths, int chars)
    {
        var batch = new List<string>();
        var length = 0;
        foreach (var path in paths)
        {
            if (batch.Count > 0 && length + path.Length + 1 > chars)
            {
                yield return batch;
                (batch, length) = ([], 0);
            }

            batch.Add(path);
            length += path.Length + 1;
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }

    /// <summary>
    /// Replaces the last commit with one of the staged changes and <paramref name="message"/> (WP-09). The old commit stays
    /// in the reflog; the caller makes sure it was not pushed (<see cref="IsOnRemoteAsync"/>, never a force push).
    /// </summary>
    public Task<GitResult> AmendAsync(string repo, string message, CancellationToken ct) =>
        Local(repo, ct, "commit", "--amend", "-m", message);

    /// <summary>
    /// A new commit that undoes <paramref name="id"/> (WP-09). On a conflict git stops and leaves the revert unfinished
    /// (<c>REVERT_HEAD</c>) until it is continued or aborted (WP-07).
    /// </summary>
    public Task<GitResult> RevertAsync(string repo, string id, CancellationToken ct) =>
        IsCommitId(id)
            ? Local(repo, ct, "revert", "--no-edit", id)
            : Task.FromResult(GitResult.Failed(TextFormat.Format(SyncText.InvalidCommit, id)));

    /// <summary>
    /// True if HEAD already has a commit with the same change as <paramref name="id"/> (same patch id, <c>git cherry</c>): a
    /// cherry-pick of it would come out empty, and git would stop and leave it unfinished (E2E WP-09). Null if git cannot
    /// tell – for a root commit, or one with several parents.
    /// </summary>
    public async Task<bool?> IsAppliedAsync(string repo, CommitDetails commit, CancellationToken ct)
    {
        if (commit.Parents is not [var parent] || !IsCommitId(commit.Id) || !IsCommitId(parent))
        {
            return null;
        }

        var result = await Local(repo, ct, "cherry", "HEAD", commit.Id, parent);
        return result.Success ? result.Output.TrimStart().StartsWith('-') : null;
    }

    /// <summary>Applies <paramref name="id"/> onto HEAD as a new commit (WP-09); on a conflict git stops as in <see cref="RevertAsync"/>.</summary>
    public Task<GitResult> CherryPickAsync(string repo, string id, CancellationToken ct) =>
        IsCommitId(id)
            ? Local(repo, ct, "cherry-pick", id)
            : Task.FromResult(GitResult.Failed(TextFormat.Format(SyncText.InvalidCommit, id)));

    // ---------- merge, rebase and unfinished operations (WP-07, ADR 0014) ----------

    private Task<GitResult> Writing(string repo, CancellationToken ct, params string[] args) =>
        runner.RunAsync(repo, args, GitCallMode.Interactive, GitRunner.CommitTimeout, null, ct);

    /// <summary>The command of an operation: <c>merge</c>, <c>rebase</c>, <c>cherry-pick</c>, <c>revert</c>.</summary>
    private static string Command(GitOperation operation) => operation switch
    {
        GitOperation.Merge => "merge",
        GitOperation.Rebase => "rebase",
        GitOperation.CherryPick => "cherry-pick",
        _ => "revert",
    };

    /// <summary>
    /// The full name of a branch from <see cref="BranchesAsync"/>: <c>refs/heads/x</c> or <c>refs/remotes/origin/x</c>. Never an
    /// option, never ambiguous with a tag or a file – and git still names it "branch 'x'" in a merge message.
    /// </summary>
    public static string RefOf(BranchInfo branch) => (branch.IsRemote ? "refs/remotes/" : "refs/heads/") + branch.Name;

    /// <summary>
    /// Merges <paramref name="branch"/> into HEAD: a fast-forward if possible, otherwise a merge commit with git's message
    /// (<c>merge.ff</c> of the user applies). On a conflict git stops and leaves the merge unfinished. The name goes into the
    /// message ("Merge branch 'feature'"), so the caller passes the short name once it is checked
    /// (<see cref="IsBranchNameAsync"/>) and names the branch's commit, or else <see cref="RefOf"/>. Never with the user's
    /// <c>merge.autoStash</c>: it would put overlapping local changes back with conflict markers (review 0.4.0, A1).
    /// </summary>
    public Task<GitResult> MergeAsync(string repo, string branch, CancellationToken ct) =>
        branch.Length > 0 && !branch.StartsWith('-')
            ? Writing(repo, ct, "merge", "--no-edit", "--no-autostash", branch)
            : Task.FromResult(GitResult.Failed(TextFormat.Format(SyncText.InvalidBranchName, branch)));

    /// <summary>
    /// Replays the commits of HEAD's branch onto <paramref name="branchRef"/> (<see cref="RefOf"/>); never interactive, never
    /// with the user's <c>rebase.autoStash</c> (as <see cref="MergeAsync"/>).
    /// </summary>
    public Task<GitResult> RebaseAsync(string repo, string branchRef, CancellationToken ct) =>
        branchRef.StartsWith("refs/", StringComparison.Ordinal)
            ? Writing(repo, ct, "rebase", "--no-autostash", branchRef)
            : Task.FromResult(GitResult.Failed(TextFormat.Format(SyncText.InvalidBranchName, branchRef)));

    /// <summary>
    /// The paths a merge of <paramref name="branchRef"/> brings (<paramref name="mergeBase"/>: since the merge base) or a rebase
    /// onto it changes (the difference between HEAD and it) – to look for ignored files git would overwrite. Null if git fails.
    /// </summary>
    public async Task<IReadOnlyList<string>?> ChangedPathsAsync(string repo, string branchRef, bool mergeBase, CancellationToken ct)
    {
        if (!branchRef.StartsWith("refs/", StringComparison.Ordinal))
        {
            return null;
        }

        var result = mergeBase
            ? await Local(repo, ct, "diff", "--name-only", "-z", "--no-renames", "--no-ext-diff", "HEAD..." + branchRef, "--")
            : await Local(repo, ct, "diff", "--name-only", "-z", "--no-renames", "--no-ext-diff", "HEAD", branchRef, "--");
        return result.Success ? result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries) : null;
    }

    /// <summary>
    /// How many of the commits a rebase onto <paramref name="branchRef"/> would rewrite (<c>branch..HEAD</c>) a remote branch
    /// already has – after the rebase, pushing them again would need a force push. Null if git cannot tell.
    /// </summary>
    public async Task<int?> CountPushedToRewriteAsync(string repo, string branchRef, CancellationToken ct)
    {
        if (!branchRef.StartsWith("refs/", StringComparison.Ordinal))
        {
            return null;
        }

        var all = await Local(repo, ct, "rev-list", "--count", branchRef + "..HEAD", "--");
        var unpushed = await Local(repo, ct, "rev-list", "--count", branchRef + "..HEAD", "--not", "--remotes", "--");
        return int.TryParse(all.Output.Trim(), out var a) && int.TryParse(unpushed.Output.Trim(), out var u) && all.Success && unpushed.Success
            ? a - u
            : null;
    }

    /// <summary>
    /// Goes on with a stopped operation once its conflicts are resolved: a merge, cherry-pick or revert commits the index with
    /// the message git prepared (no editor, see <see cref="GitRunner"/>), a rebase also replays the commits still to come.
    /// </summary>
    public Task<GitResult> ContinueAsync(string repo, GitOperation operation, CancellationToken ct) =>
        Writing(repo, ct, Command(operation), "--continue");

    /// <summary>
    /// Leaves the commit a rebase, cherry-pick or revert stopped at out and goes on with the next (ADR 0014): what was
    /// resolved for it is discarded – the caller asks first. A merge has nothing to skip.
    /// </summary>
    public Task<GitResult> SkipAsync(string repo, GitOperation operation, CancellationToken ct) =>
        operation == GitOperation.Merge
            ? Task.FromResult(GitResult.Failed(SyncText.Skip_Merge))
            : Writing(repo, ct, Command(operation), "--skip");

    /// <summary>
    /// Ends a stopped operation and returns to the state before it (ADR 0014, the exception to CLAUDE.md §2): the index and the
    /// files it touched go back, resolved conflicts with them. git refuses rather than overwrite an untracked file. The caller
    /// asks first and checks that it is still the operation the question named (<see cref="OperationInfo.Key"/>).
    /// </summary>
    public Task<GitResult> AbortAsync(string repo, GitOperation operation, CancellationToken ct) =>
        // The longer limit too: ended halfway, a reset leaves a half-restored worktree and index.lock (review WP-07).
        Writing(repo, ct, Command(operation), "--abort");

    /// <summary>The files staged as new against HEAD (<c>diff --cached --diff-filter=A</c>); null if git fails.</summary>
    public async Task<IReadOnlyList<string>?> AddedInIndexAsync(string repo, CancellationToken ct)
    {
        var result = await Local(repo, ct, "diff", "--cached", "--name-only", "-z", "--no-renames", "--diff-filter=A", "--no-ext-diff", "HEAD", "--");
        return result.Success ? result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries) : null;
    }

    /// <summary>
    /// The files the unfinished <paramref name="operation"/> brings as new: a merge those added on the other side since the
    /// merge base, a cherry-pick or rebase step those its commit adds, a revert those its commit deleted. Null if git fails
    /// (a root commit has no parent).
    /// </summary>
    public async Task<IReadOnlyList<string>?> AddedByAsync(string repo, GitOperation operation, string commit, CancellationToken ct)
    {
        if (!IsCommitId(commit))
        {
            return null;
        }

        string[] range = operation switch
        {
            GitOperation.Merge => ["HEAD..." + commit],
            GitOperation.Revert => [commit, commit + "^"],
            _ => [commit + "^", commit],
        };
        var result = await Local(repo, ct, ["diff", "--name-only", "-z", "--no-renames", "--diff-filter=A", "--no-ext-diff", .. range, "--"]);
        return result.Success ? result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries) : null;
    }

    /// <summary>
    /// Takes new files out of the index, never out of the worktree (<c>rm --cached</c>): untracked, an abort or skip leaves them
    /// alone (review WP-07). <c>-f</c> because git refuses a file edited again after staging ("staged content different from
    /// both the file and the HEAD"); with <c>--cached</c> it still only touches the index (second review pass).
    /// </summary>
    public Task<GitResult> UntrackAsync(string repo, IReadOnlyList<string> paths, CancellationToken ct) =>
        InBatchesAsync(paths, batch => Local(repo, ct, [LiteralPaths, "rm", "--cached", "-f", "-q", "--", .. batch]));

    /// <summary>
    /// Which of <paramref name="paths"/> differ in the index from the version the unfinished <paramref name="operation"/> brings
    /// (<paramref name="commit"/>; a revert brings its parent's): a new file of the operation the user edited and staged again
    /// (second review pass WP-07). Null if git fails.
    /// </summary>
    public async Task<IReadOnlyList<string>?> DifferFromIncomingAsync(string repo, GitOperation operation, string commit,
        IReadOnlyList<string> paths, CancellationToken ct)
    {
        if (!IsCommitId(commit))
        {
            return null;
        }

        var rev = operation == GitOperation.Revert ? commit + "^" : commit;
        var differ = new List<string>();
        foreach (var batch in Batches(paths, 16_000))
        {
            var result = await Local(repo, ct, [LiteralPaths, "diff", "--cached", "--name-only", "-z", "--no-renames", "--no-ext-diff", rev, "--", .. batch]);
            if (!result.Success)
            {
                return null;
            }

            differ.AddRange(result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries));
        }

        return differ;
    }

    /// <summary>True if the ref exists, whatever it points to (a tag may name a tree or blob); null if git cannot tell.</summary>
    public async Task<bool?> RefExistsAsync(string repo, string fullRef, CancellationToken ct)
    {
        if (!fullRef.StartsWith("refs/", StringComparison.Ordinal))
        {
            return null;
        }

        var result = await Local(repo, ct, "show-ref", "--verify", "--quiet", fullRef);
        return result.ExitCode switch { 0 => true, 1 => false, _ => null };
    }

    /// <summary>
    /// True if git still finds conflict markers in the conflicted file <paramref name="path"/> (<c>diff --check</c>, which
    /// knows the marker size of <c>conflict-marker-size</c>); whitespace rules are switched off so that only markers count.
    /// Null if git cannot tell.
    /// </summary>
    public async Task<bool?> HasConflictMarkersAsync(string repo, string path, CancellationToken ct)
    {
        var result = await Local(repo, ct, "-c", "core.whitespace=-blank-at-eol,-blank-at-eof,-space-before-tab,-indent-with-non-tab,-tab-in-indent,-cr-at-eol",
            LiteralPaths, "diff", "--check", "--no-color", "--no-ext-diff", "--", path);
        return result.ExitCode switch
        {
            0 => false,
            2 when result.Output.Length > 0 => true,
            _ => null,
        };
    }

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
