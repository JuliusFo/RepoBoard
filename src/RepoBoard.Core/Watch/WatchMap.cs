using System.Collections.Immutable;
using RepoBoard.Core.Board;
using RepoBoard.Core.IO;

namespace RepoBoard.Core.Watch;

/// <summary>
/// The repositories and worktrees on the board as the file watchers see them (WP-03, ADR 0011): which folders to watch and
/// whose status a changed path makes stale. Built from a snapshot of the board; pure, no file system access.
/// </summary>
internal sealed class WatchMap
{
    /// <summary>
    /// Folders whose files git ignores in nearly every repository and a build writes by the thousand: a change in them
    /// reads nothing. A tracked one shows with the fallback tick (proposal, not confirmed: a fixed list).
    /// </summary>
    internal static readonly ImmutableHashSet<string> IgnoredFolders =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "bin", "obj", "node_modules", ".vs", ".idea", "TestResults");

    /// <summary>
    /// In a git folder only these change a status – a positive list (review WP-03): RepoBoard's own <c>git status</c> writes
    /// there with fsmonitor (<c>fsmonitor--daemon/cookies</c>) or Git LFS (<c>lfs/tmp</c>), and with a list of what to
    /// ignore every such file would read the status again, forever. Objects and reflogs are left out by it too.
    /// </summary>
    private static readonly ImmutableHashSet<string> StateFiles = ImmutableHashSet.Create(StringComparer.Ordinal,
        "HEAD", "index", "FETCH_HEAD", "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "REBASE_HEAD", "packed-refs", "config");

    /// <inheritdoc cref="StateFiles"/>
    private static readonly ImmutableHashSet<string> StateFolders = ImmutableHashSet.Create(StringComparer.Ordinal,
        "refs", "reftable", "rebase-merge", "rebase-apply", "sequencer", "info");

    /// <summary>
    /// What a repository shares with its worktrees: refs (also as reftable, git 2.45), branches and upstreams (config),
    /// excludes (info).
    /// </summary>
    private static readonly ImmutableHashSet<string> SharedState = ImmutableHashSet.Create(StringComparer.Ordinal,
        "refs", "reftable", "packed-refs", "config", "info");


    private readonly ImmutableArray<Node> _nodes;
    private readonly ImmutableArray<Signature> _signature;

    public static readonly WatchMap Empty = new([], []);

    private WatchMap(ImmutableArray<Node> nodes, ImmutableArray<Signature> signature)
    {
        _nodes = nodes;
        _signature = signature;
        var folders = nodes.SelectMany(n => n.GitDir is { } gitDir ? [n.Path, gitDir] : new[] { n.Path })
            .Distinct(Paths.Comparer).ToList();
        Roots = [.. folders.Where(folder => !folders.Any(other => !SamePath(folder, other) && IsUnder(folder, other)))];
    }

    /// <summary>The folders to watch: every repository's and worktree's folder and git folder that no other one contains.</summary>
    public ImmutableArray<string> Roots { get; }

    /// <summary>Every repository and worktree of the snapshot.</summary>
    public IEnumerable<RepoState> States => _nodes.Select(n => n.State);

    public static WatchMap Of(IEnumerable<RepoState> repos)
    {
        var nodes = ImmutableArray.CreateBuilder<Node>();
        var signature = ImmutableArray.CreateBuilder<Signature>();
        foreach (var repo in repos)
        {
            signature.Add(new(repo, repo.GitDir, repo.Missing, repo.Worktrees));
            if (Normalize(repo.Path) is not { } path)
            {
                continue;
            }

            // Until the first status: a repository's git folder is most likely ".git" in it.
            nodes.Add(new(repo, path, Normalize(repo.GitDir) ?? Path.Combine(path, ".git"), repo));
            foreach (var worktree in repo.Worktrees)
            {
                signature.Add(new(worktree, worktree.GitDir, worktree.Missing, []));
                if (Normalize(worktree.Path) is { } worktreePath)
                {
                    nodes.Add(new(worktree, worktreePath, Normalize(worktree.GitDir), repo));
                }
            }
        }

        return new(nodes.ToImmutable(), signature.ToImmutable());
    }

    /// <summary>
    /// Whether <paramref name="repos"/> is still what this map was built from – the same repositories and worktrees, git
    /// folders and missing folders. Cheap: compares references, so the board's every change need not build a new map.
    /// </summary>
    public bool Matches(IEnumerable<RepoState> repos)
    {
        var i = 0;
        foreach (var repo in repos)
        {
            if (!Same(i++, repo, repo.Worktrees))
            {
                return false;
            }

            foreach (var worktree in repo.Worktrees)
            {
                if (!Same(i++, worktree, []))
                {
                    return false;
                }
            }
        }

        return i == _signature.Length;

        bool Same(int index, RepoState state, ImmutableList<RepoState> worktrees) =>
            index < _signature.Length && _signature[index] is var s && ReferenceEquals(s.State, state)
            && ReferenceEquals(s.GitDir, state.GitDir) && s.Missing == state.Missing && ReferenceEquals(s.Worktrees, worktrees);
    }

    /// <summary>
    /// Whose status <paramref name="path"/> changing makes stale; empty when nothing on the board depends on it.
    /// </summary>
    public IReadOnlyList<RepoState> Route(string path)
    {
        if (Normalize(path) is not { } changed)
        {
            return [];
        }

        // A git folder first: a worktree's (".git/worktrees/<name>") lies in its repository's, the deepest one wins.
        if (Deepest(changed, n => n.GitDir) is { } inGit)
        {
            var parts = Parts(changed, inGit.GitDir!);
            if (inGit.State.IsWorktree)
            {
                // Its git folder itself going or coming: the repository reads its list of worktrees again.
                return parts.Length == 0 ? [inGit.Main] : ChangesStatus(parts) ? [inGit.State] : [];
            }

            return parts switch
            {
                [] => [],
                // Shared: a commit, fetch, new branch or upstream changes ahead and behind in every worktree too.
                [var first, ..] when SharedState.Contains(first) && ChangesStatus(parts) => [inGit.Main, .. inGit.Main.Worktrees],
                // The stash list (WP-06): dropping an entry below the newest rewrites only its reflog. Shared like the refs.
                ["logs", "refs", "stash"] => [inGit.Main, .. inGit.Main.Worktrees],
                // A worktree added or removed, or one whose git folder is not known yet.
                ["worktrees", _] => [inGit.Main],
                ["worktrees", _, .. var rest] => ChangesStatus(rest) ? [inGit.Main] : [],
                // A submodule's git folder (one on the board has its own node): its HEAD or refs change the parent's status.
                ["modules", .. var rest] => SubmoduleChanged(rest) ? [inGit.Main] : [],
                _ => ChangesStatus(parts) ? [inGit.Main] : [],
            };
        }

        if (Deepest(changed, n => n.Path) is not { } inTree)
        {
            return [];
        }

        // ".git" here is a linked worktree's pointer file or a repository nested in this one, not on the board.
        var segments = Parts(changed, inTree.Path);
        return segments.Length == 0 || segments.Any(s => s.Equals(".git", Paths.Comparison) || IgnoredFolders.Contains(s)) ? [] : [inTree.State];
    }

    /// <summary>The repositories and worktrees inside <paramref name="root"/>, folder and git folder: what a watcher on it sees.</summary>
    public IEnumerable<RepoState> Under(string root)
    {
        var normalized = Normalize(root);
        return normalized is null ? [] : _nodes.Where(n => IsUnder(n.Path, normalized) || (n.GitDir is { } g && IsUnder(g, normalized))).Select(n => n.State);
    }

    /// <summary>Whether watchers on <paramref name="watched"/> see both the folder and the git folder of <paramref name="state"/>.</summary>
    public bool IsCovered(RepoState state, IReadOnlyCollection<string> watched) =>
        _nodes.FirstOrDefault(n => ReferenceEquals(n.State, state)) is { } node
        && watched.Any(root => IsUnder(node.Path, root))
        && (node.GitDir is not { } gitDir || watched.Any(root => IsUnder(gitDir, root)));

    /// <summary>Whether a path in a git folder is one of its <see cref="StateFiles"/> or in one of its <see cref="StateFolders"/>.</summary>
    private static bool ChangesStatus(ReadOnlySpan<string> parts) =>
        parts.Length > 0 && !parts[^1].EndsWith(".lock", StringComparison.Ordinal)
        && (parts.Length == 1 ? StateFiles.Contains(parts[0]) || StateFolders.Contains(parts[0]) : StateFolders.Contains(parts[0]));

    /// <summary>
    /// Below <c>.git/modules</c>, whose names may hold slashes: the submodule's HEAD, index or refs – not its reflogs, objects
    /// or what its own status writes.
    /// </summary>
    private static bool SubmoduleChanged(string[] rest) =>
        rest.Length > 0 && !rest[^1].EndsWith(".lock", StringComparison.Ordinal) && !rest.Contains("logs")
        && (rest[^1] is "HEAD" or "index" or "packed-refs" || rest.Contains("refs"));

    private Node? Deepest(string path, Func<Node, string?> folder)
    {
        Node? found = null;
        foreach (var node in _nodes)
        {
            if (folder(node) is { } candidate && IsUnder(path, candidate) && (found is null || candidate.Length > folder(found)!.Length))
            {
                found = node;
            }
        }

        return found;
    }

    private static string[] Parts(string path, string root) =>
        path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

    private static bool IsUnder(string path, string folder) => Paths.IsUnder(path, folder);

    private static bool SamePath(string a, string b) => string.Equals(a, b, Paths.Comparison);

    internal static string? Normalize(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <param name="Main">The repository on the board: <paramref name="State"/> itself or the one it is a worktree of.</param>
    private sealed record Node(RepoState State, string Path, string? GitDir, RepoState Main);

    private readonly record struct Signature(RepoState State, string? GitDir, bool Missing, ImmutableList<RepoState> Worktrees);
}
