using RepoBoard.Core.Board;
using RepoBoard.Core.Git;
using RepoBoard.Core.Repos;
using RepoBoard.UI.State;

namespace RepoBoard.UI.Tests.State;

public sealed class BoardFilterTests
{
    private static RepoState Repo(string name) => new(new RepoEntry(Path.Combine(Path.GetTempPath(), name)));

    private static RepoStatus Status(string? branch = "main", string? upstream = "origin/main", int ahead = 0, int behind = 0, bool changed = false) =>
        new(branch, "abc", upstream, ahead, behind, changed ? [new FileChange("a.txt", null, '.', 'M', false, false)] : []);

    /// <summary>Each chip asks one question; a never pushed branch counts as ahead (everything on it is only local).</summary>
    [Theory]
    [InlineData(BoardFilterKind.All, "clean,changed,behind,ahead,unpushed,detached")]
    [InlineData(BoardFilterKind.Changes, "changed")]
    [InlineData(BoardFilterKind.Behind, "behind")]
    [InlineData(BoardFilterKind.Ahead, "ahead,unpushed")]
    public void A_chip_shows_the_repositories_that_answer_its_question(BoardFilterKind kind, string expected)
    {
        var repos = new Dictionary<string, RepoStatus>
        {
            ["clean"] = Status(),
            ["changed"] = Status(changed: true),
            ["behind"] = Status(behind: 2),
            ["ahead"] = Status(ahead: 1),
            ["unpushed"] = Status(upstream: null),
            ["detached"] = Status(branch: null, upstream: null),
        }.Select(p => WithStatus(Repo(p.Key), p.Value));

        Assert.Equal(expected, string.Join(',', BoardFilter.Visible(repos, kind, "").Select(r => r.Name)));
    }

    [Theory]
    [InlineData("ferret", true)] // name
    [InlineData("FEATURE/x", true)] // branch, any case
    [InlineData("other", false)]
    [InlineData("  ", true)]
    public void The_search_looks_at_name_path_and_branch(string search, bool found) =>
        Assert.Equal(found, BoardFilter.MatchesSearch(search, WithStatus(Repo("FerretSharp"), Status(branch: "feature/x"))));

    /// <summary>
    /// WP-04: a worktree answers the chips like a repository; its repository shows with it, the worktree even in a closed
    /// group. The search does not look at a worktree's path – it holds its repository's, and every worktree would open.
    /// </summary>
    [Theory]
    [InlineData(BoardFilterKind.All, "", false, "repo:", 1)]
    [InlineData(BoardFilterKind.All, "", true, "repo:wt-clean,wt-changed", 1)]
    [InlineData(BoardFilterKind.Changes, "", false, "repo:wt-changed", 1)]
    [InlineData(BoardFilterKind.Changes, "", true, "repo:wt-clean,wt-changed", 1)]
    [InlineData(BoardFilterKind.Behind, "", false, "", 0)]
    [InlineData(BoardFilterKind.AutoSync, "", false, "", 0)]
    [InlineData(BoardFilterKind.All, "wt-ch", false, "repo:wt-changed", 1)]
    [InlineData(BoardFilterKind.All, "repo", false, "repo:", 1)]
    public void Worktrees_answer_the_chips_and_the_search(BoardFilterKind kind, string search, bool groupOpen, string expected, int count)
    {
        var repo = WithStatus(Repo("repo"), Status());
        repo.Worktrees =
        [
            WithStatus(new RepoState(new RepoEntry(Path.Combine(repo.Path, ".claude", "worktrees", "wt-clean")), repo), Status(branch: "a")),
            WithStatus(new RepoState(new RepoEntry(Path.Combine(repo.Path, "..", "wt-changed")), repo), Status(branch: "b", changed: true)),
        ];

        var rows = BoardFilter.Visible([repo], kind, search)
            .Select(r => $"{r.Name}:{string.Join(',', BoardFilter.VisibleWorktrees(r, groupOpen, kind, search).Select(w => w.Name))}");

        Assert.Equal(expected, string.Join(';', rows));
        Assert.Equal(kind == BoardFilterKind.All ? 1 : count, BoardFilter.Count([repo], kind));
    }

    private static RepoState WithStatus(RepoState repo, RepoStatus status)
    {
        repo.Status = status; // set by the board only (internal, visible to the tests)
        return repo;
    }
}
