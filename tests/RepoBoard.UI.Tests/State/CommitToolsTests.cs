using RepoBoard.Core.Board;
using RepoBoard.Core.Git;
using RepoBoard.Core.Repos;
using RepoBoard.Core.Resources;
using RepoBoard.UI.State;

namespace RepoBoard.UI.Tests.State;

/// <summary>When the buttons of amend, revert and cherry-pick are open (WP-09).</summary>
public sealed class CommitToolsTests
{
    private static RepoState Repo(IReadOnlyList<FileChange>? changes = null, bool initial = false, bool onRemote = false,
        GitOperation? operation = null) => new(new RepoEntry("/r"))
    {
        Status = new RepoStatus("main", initial ? null : "abc", "origin/main", 0, 0, changes ?? []),
        Details = new RepoDetails([], [], [], false, onRemote),
        Operation = operation,
    };

    private static CommitDetails Commit(bool inHead, int parents = 1, bool applied = false) =>
        new("abc", "abc", [.. Enumerable.Range(0, parents).Select(i => $"p{i}")], "Jo", "jo@x", default, "Jo", default, "", "Subject", [], inHead, applied);

    private static readonly FileChange Changed = new("a.txt", null, '.', 'M', false, false);
    private static readonly FileChange New = new("n.txt", null, '.', '?', true, false);

    [Fact]
    public void Amend_is_open_only_for_an_unpushed_commit_without_an_operation()
    {
        Assert.Null(CommitTools.AmendBlocker(Repo([Changed])));
        Assert.Equal(SyncText.Amend_Pushed, CommitTools.AmendBlocker(Repo(onRemote: true)));
        Assert.Equal(SyncText.Pick_Initial, CommitTools.AmendBlocker(Repo(initial: true)));
        Assert.NotNull(CommitTools.AmendBlocker(Repo(operation: GitOperation.Merge)));
        Assert.Equal(SyncText.Pick_Unknown, CommitTools.AmendBlocker(new RepoState(new RepoEntry("/r"))));
    }

    [Fact]
    public void Revert_and_cherry_pick_need_a_clean_worktree_and_the_right_side_of_head()
    {
        Assert.Null(CommitTools.PickBlocker(Repo([New]), Commit(inHead: true), revert: true));
        Assert.Null(CommitTools.PickBlocker(Repo(), Commit(inHead: false), revert: false));
        Assert.Equal(SyncText.Pick_Changes, CommitTools.PickBlocker(Repo([Changed]), Commit(inHead: true), revert: true));
        Assert.Equal(SyncText.Revert_NotInHead, CommitTools.PickBlocker(Repo(), Commit(inHead: false), revert: true));
        Assert.Equal(SyncText.CherryPick_InHead, CommitTools.PickBlocker(Repo(), Commit(inHead: true), revert: false));
        Assert.Equal(SyncText.CherryPick_Applied, CommitTools.PickBlocker(Repo(), Commit(inHead: false, applied: true), revert: false));
        Assert.Equal(SyncText.Pick_Merge, CommitTools.PickBlocker(Repo(), Commit(inHead: true, parents: 2), revert: true));
        Assert.NotNull(CommitTools.PickBlocker(Repo(operation: GitOperation.Revert), Commit(inHead: true), revert: true));
    }
}
