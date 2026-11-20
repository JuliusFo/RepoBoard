using RepoBoard.Core.Board;

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

/// <summary>Which repositories the table shows: one chip and the search text (name, path or branch).</summary>
public static class BoardFilter
{
    public static IReadOnlyList<BoardFilterKind> Kinds { get; } = Enum.GetValues<BoardFilterKind>();

    public static bool Matches(BoardFilterKind kind, RepoState repo) => kind switch
    {
        BoardFilterKind.Changes => repo.Status is { IsClean: false },
        BoardFilterKind.Behind => repo.Status is { Behind: > 0 },
        // Never pushed counts as ahead: everything on it is only local.
        BoardFilterKind.Ahead => repo.Status is { Ahead: > 0 } or { HasUpstream: false, IsDetached: false },
        BoardFilterKind.AutoSync => repo.AutoSyncOn,
        BoardFilterKind.Problems => repo.Error is not null || repo.Missing || repo.Operation is not null || repo.AutoPullBlocked is not null,
        _ => true,
    };

    public static bool MatchesSearch(string search, RepoState repo) =>
        string.IsNullOrWhiteSpace(search)
        || repo.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
        || repo.Path.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
        || (repo.Status?.Branch?.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) ?? false);

    public static IEnumerable<RepoState> Visible(IEnumerable<RepoState> repos, BoardFilterKind kind, string search) =>
        repos.Where(r => Matches(kind, r) && MatchesSearch(search, r));
}
