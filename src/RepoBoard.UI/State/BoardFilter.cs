using RepoBoard.Core.Board;
using RepoBoard.Core.Git;

namespace RepoBoard.UI.State;

/// <summary>The chips above the table: each a question about a repository; the count on the chip is how many answer yes.</summary>
public enum BoardFilterKind
{
    All,
    Changes,
    Behind,
    Ahead,
    AutoSync,
    Problems,
}

/// <summary>
/// Which rows the table shows: one chip and the search text (name, path or branch). Worktrees (WP-04) answer the same
/// questions; a repository is shown when it or one of its worktrees answers yes, and the worktrees that do are shown
/// under it even when its group is closed.
/// </summary>
public static class BoardFilter
{
    public static IReadOnlyList<BoardFilterKind> Kinds { get; } = Enum.GetValues<BoardFilterKind>();

    public static bool Matches(BoardFilterKind kind, RepoState repo) => kind switch
    {
        BoardFilterKind.Changes => repo.Status is { IsClean: false },
        BoardFilterKind.Behind => repo.Status is { Behind: > 0 },
        // Never pushed counts as ahead: everything on it is only local.
        BoardFilterKind.Ahead => HasToPush(repo.Status),
        BoardFilterKind.AutoSync => repo.AutoSyncOn,
        BoardFilterKind.Problems => repo.Error is not null || repo.Missing || repo.Operation is not null || repo.AutoPullBlocked is not null,
        _ => true,
    };

    /// <summary>
    /// Name, path or branch. A worktree's path is not searched: it mostly holds its repository's (<c>.claude\worktrees</c>),
    /// and searching for the repository would open every worktree.
    /// </summary>
    public static bool MatchesSearch(string search, RepoState repo) =>
        string.IsNullOrWhiteSpace(search)
        || repo.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
        || (!repo.IsWorktree && repo.Path.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
        || (repo.Status?.Branch?.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>A chip other than "all" or a search: matching worktrees show without opening their group.</summary>
    public static bool IsNarrowed(BoardFilterKind kind, string search) => kind != BoardFilterKind.All || !string.IsNullOrWhiteSpace(search);

    private static bool Shows(RepoState row, BoardFilterKind kind, string search) => Matches(kind, row) && MatchesSearch(search, row);

    public static IEnumerable<RepoState> Visible(IEnumerable<RepoState> repos, BoardFilterKind kind, string search) =>
        repos.Where(r => Shows(r, kind, search) || (IsNarrowed(kind, search) && r.Worktrees.Any(w => Shows(w, kind, search))));

    /// <summary>The worktree rows under <paramref name="repo"/>: all of an opened group, else those the filter or search finds.</summary>
    public static IEnumerable<RepoState> VisibleWorktrees(RepoState repo, bool groupOpen, BoardFilterKind kind, string search) =>
        repo.Worktrees.Where(w => groupOpen || (IsNarrowed(kind, search) && Shows(w, kind, search)));

    /// <summary>Every worktree row the table shows, under every repository it shows.</summary>
    public static IEnumerable<RepoState> ShownWorktrees(IEnumerable<RepoState> repos, Func<RepoState, bool> groupOpen, BoardFilterKind kind, string search) =>
        Visible(repos, kind, search).SelectMany(r => VisibleWorktrees(r, groupOpen(r), kind, search));

    /// <summary>The number on a chip: repositories for "all", every row that answers yes – worktrees too – for the others.</summary>
    public static int Count(IEnumerable<RepoState> repos, BoardFilterKind kind) =>
        kind == BoardFilterKind.All
            ? repos.Count()
            : repos.Sum(r => (Matches(kind, r) ? 1 : 0) + r.Worktrees.Count(w => Matches(kind, w)));

    /// <summary>
    /// Commits to push: ahead of the upstream, or a branch never pushed – but not one without a commit, whose push git
    /// refuses ("src refspec does not match"; review 0.4.0, A26). The "ahead" chip and the push button's highlight.
    /// </summary>
    public static bool HasToPush(RepoStatus? status) => status is { Ahead: > 0 } or { HasUpstream: false, IsDetached: false, IsInitial: false };
}
