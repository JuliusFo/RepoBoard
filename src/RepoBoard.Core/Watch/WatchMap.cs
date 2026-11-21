using System.Collections.Immutable;
using RepoBoard.Core.Board;

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

    /// <summary>In a git folder: object files and reflogs change with every commit or fetch next to what matters.</summary>
    private static readonly ImmutableHashSet<string> IgnoredInGitDir =
        ImmutableHashSet.Create(StringComparer.Ordinal, "objects", "logs");

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly ImmutableArray<Node> _nodes;
    private readonly ImmutableArray<Signature> _signature;

    public static readonly WatchMap Empty = new([], []);

    private WatchMap(ImmutableArray<Node> nodes, ImmutableArray<Signature> signature)
    {
        _nodes = nodes;
        _signature = signature;
        var folders = nodes.SelectMany(n => n.GitDir is { } gitDir ? [n.Path, gitDir] : new[] { n.Path })
            .Distinct(StringComparer.FromComparison(PathComparison)).ToList();
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
            if (parts.Length == 0 || IgnoredInGitDir.Contains(parts[0]) || parts[^1].EndsWith(".lock", StringComparison.Ordinal))
            {
                return [];
            }

            if (inGit.State.IsWorktree)
            {
                return [inGit.State];
            }

            // The refs are shared: a commit, fetch or new branch changes ahead and behind in every worktree too.
            return parts[0] is "refs" or "packed-refs" ? [inGit.Main, .. inGit.Main.Worktrees] : [inGit.Main];
        }

        if (Deepest(changed, n => n.Path) is not { } inTree)
        {
            return [];
        }

        // ".git" here is a linked worktree's pointer file or a repository nested in this one, not on the board.
        var segments = Parts(changed, inTree.Path);
        return segments.Length == 0 || segments.Any(s => s.Equals(".git", PathComparison) || IgnoredFolders.Contains(s)) ? [] : [inTree.State];
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

    internal static bool IsUnder(string path, string folder) =>
        path.StartsWith(folder, PathComparison)
        && (path.Length == folder.Length || path[folder.Length] == Path.DirectorySeparatorChar || Path.EndsInDirectorySeparator(folder));

    private static bool SamePath(string a, string b) => string.Equals(a, b, PathComparison);

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
