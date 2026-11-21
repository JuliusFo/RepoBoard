using RepoBoard.Core.Board;
using RepoBoard.Core.Repos;
using RepoBoard.Core.Watch;

namespace RepoBoard.Core.Tests.Watch;

/// <summary>Which change makes whose status stale (WP-03, ADR 0011) – pure, on paths that need not exist.</summary>
public sealed class WatchMapTests
{
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "watch-map");
    private static readonly string Repo = Path.Combine(Base, "repo");
    private static readonly string Inner = Path.Combine(Repo, ".claude", "worktrees", "inner");
    private static readonly string Beside = Path.Combine(Base, "beside");
    private static readonly string Nested = Path.Combine(Repo, "nested");

    /// <summary>
    /// "repo" with a worktree inside it (".claude/worktrees/inner") and one beside it, and "nested", a repository of its own
    /// on the board inside "repo".
    /// </summary>
    private static (WatchMap Map, Dictionary<string, RepoState> States) Board()
    {
        var repo = new RepoState(new RepoEntry(Repo)) { GitDir = Path.Combine(Repo, ".git") };
        var inner = new RepoState(new RepoEntry(Inner), repo) { GitDir = Path.Combine(Repo, ".git", "worktrees", "inner") };
        var beside = new RepoState(new RepoEntry(Beside), repo) { GitDir = Path.Combine(Repo, ".git", "worktrees", "beside") };
        repo.Worktrees = [inner, beside];
        var nested = new RepoState(new RepoEntry(Nested)); // git folder not read yet
        var states = new Dictionary<string, RepoState> { ["repo"] = repo, ["inner"] = inner, ["beside"] = beside, ["nested"] = nested };
        return (WatchMap.Of([nested, repo]), states);
    }

    [Theory]
    // work files: the deepest repository or worktree they are in
    [InlineData("repo/src/a.cs", "repo")]
    [InlineData("repo/.claude/worktrees/inner/src/a.cs", "inner")]
    [InlineData("beside/a.cs", "beside")]
    [InlineData("repo/nested/a.cs", "nested")]
    [InlineData("repo/.claude/settings.json", "repo")]
    // the git folder: what only the repository has, the shared refs, a worktree's own git folder
    [InlineData("repo/.git/HEAD", "repo")]
    [InlineData("repo/.git/index", "repo")]
    [InlineData("repo/.git/FETCH_HEAD", "repo")]
    [InlineData("repo/.git/rebase-merge/done", "repo")]
    [InlineData("repo/.git/refs/heads/main", "repo inner beside")]
    [InlineData("repo/.git/packed-refs", "repo inner beside")]
    [InlineData("repo/.git/config", "repo inner beside")] // an upstream set in a worktree (review WP-03)
    [InlineData("repo/.git/info/exclude", "repo inner beside")]
    [InlineData("repo/.git/MERGE_HEAD", "repo")]
    [InlineData("repo/.git/sequencer/todo", "repo")]
    [InlineData("repo/.git/modules/lib/foo/HEAD", "repo")] // a submodule's commit changes its parent's status
    [InlineData("repo/.git/modules/lib/foo/refs/heads/main", "repo")]
    [InlineData("repo/.git/worktrees/inner", "repo")] // a worktree's git folder removed: the repository reads its list
    [InlineData("repo/.git/worktrees/inner/HEAD", "inner")]
    [InlineData("repo/.git/worktrees/beside/index", "beside")]
    [InlineData("repo/.git/worktrees/new-one", "repo")] // a worktree being added: the repository reads its list
    [InlineData("repo/nested/.git/HEAD", "nested")] // a git folder not read yet is ".git"
    // nothing to read
    [InlineData("repo/.git/objects/ab/cdef", "")]
    [InlineData("repo/.git/logs/HEAD", "")]
    [InlineData("repo/.git/index.lock", "")]
    [InlineData("repo/.git/refs/heads/main.lock", "")]
    [InlineData("repo/.git/worktrees/inner/logs/HEAD", "")]
    // what RepoBoard's own status writes with fsmonitor or Git LFS – read again, it would never stop (review WP-03)
    [InlineData("repo/.git/fsmonitor--daemon/cookies/1234-1", "")]
    [InlineData("repo/.git/lfs/tmp/123456", "")]
    [InlineData("repo/.git/worktrees/inner/fsmonitor--daemon/cookies/1-2", "")]
    [InlineData("repo/.git/modules/lib/foo/fsmonitor--daemon/cookies/1-2", "")]
    [InlineData("repo/.git/modules/lib/foo/logs/refs/heads/main", "")]
    [InlineData("repo/.git/ORIG_HEAD", "")]
    [InlineData("repo/.git/COMMIT_EDITMSG", "")]
    [InlineData("repo/.git/ms-persist.xml", "")]
    [InlineData("repo/.git/gk/config", "")]
    [InlineData("repo/.git", "")]
    [InlineData("repo", "")]
    [InlineData("repo/src/bin/Debug/a.dll", "")]
    [InlineData("repo/obj/project.assets.json", "")]
    [InlineData("repo/web/node_modules/x/index.js", "")]
    [InlineData("repo/.vs/RepoBoard/v17/.suo", "")]
    [InlineData("repo/.claude/worktrees/inner/.git", "")] // a linked worktree's pointer file
    [InlineData("repo/vendor/lib/.git/HEAD", "")] // a repository inside, not on the board
    [InlineData("elsewhere/a.cs", "")]
    public void A_change_makes_stale_whose_status_it_is_in(string changed, string expected)
    {
        var (map, states) = Board();
        var path = Path.Combine([Base, .. changed.Split('/')]);

        var stale = map.Route(path).Select(s => states.Single(p => p.Value == s).Key);

        Assert.Equal(expected, string.Join(' ', stale));
    }

    [Fact]
    public void The_watched_folders_are_those_no_other_contains()
    {
        var (map, _) = Board();

        // "inner", "nested" and the worktrees' git folders lie in "repo"
        Assert.Equal([Beside, Repo], map.Roots.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_watcher_on_a_folder_sees_the_repositories_and_worktrees_in_it()
    {
        var (map, states) = Board();

        // "beside" too: its git folder lies in the repository's
        Assert.Equal(["beside", "inner", "nested", "repo"], map.Under(Repo).Select(s => states.Single(p => p.Value == s).Key).Order(StringComparer.Ordinal));
        Assert.Equal(["beside"], map.Under(Beside).Select(s => states.Single(p => p.Value == s).Key));
    }

    [Fact]
    public void A_repository_whose_git_folder_lies_outside_is_watched_only_with_both_folders()
    {
        var gitDir = Path.Combine(Base, "modules", "sub");
        var sub = new RepoState(new RepoEntry(Path.Combine(Base, "sub"))) { GitDir = gitDir };
        var map = WatchMap.Of([sub]);

        Assert.Contains(gitDir, map.Roots);
        Assert.Equal([sub], map.Route(Path.Combine(gitDir, "HEAD")));
        Assert.False(map.IsCovered(sub, [sub.Path]));
        Assert.True(map.IsCovered(sub, [sub.Path, gitDir]));
    }

    [Fact]
    public void The_map_is_built_again_only_when_the_board_changed_what_it_watches()
    {
        var (_, states) = Board();
        List<RepoState> repos = [states["nested"], states["repo"]];
        var map = WatchMap.Of(repos);

        Assert.True(map.Matches(repos));
        states["repo"].Status = null; // a new status changes nothing the watchers see

        Assert.True(map.Matches(repos));
        states["nested"].GitDir = Path.Combine(Nested, ".git");
        Assert.False(map.Matches(repos));

        map = WatchMap.Of(repos);
        states["beside"].Missing = true;
        Assert.False(map.Matches(repos));

        map = WatchMap.Of(repos);
        states["repo"].Worktrees = [states["inner"]];
        Assert.False(map.Matches(repos));

        map = WatchMap.Of(repos);
        Assert.False(map.Matches([states["repo"]]));
    }
}
