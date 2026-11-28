using RepoBoard.Core.Board;
using RepoBoard.Core.Git;
using RepoBoard.Core.Repos;
using RepoBoard.Core.Resources;
using RepoBoard.UI.Resources;
using RepoBoard.UI.State;

namespace RepoBoard.UI.Tests.State;

/// <summary>The bar of an unfinished operation and the merge and rebase buttons (WP-07).</summary>
public sealed class OperationToolsTests
{
    private static readonly FileChange Conflict = new("a.txt", null, 'U', 'U', false, true);
    private static readonly FileChange Changed = new("b.txt", null, '.', 'M', false, false);
    private static readonly FileChange StagedOnly = new("c.txt", null, 'M', '.', false, false);
    private static readonly FileChange New = new("n.txt", null, '.', '?', true, false);

    private static RepoState Repo(IReadOnlyList<FileChange>? changes = null, GitOperation? operation = null, string? branch = "main",
        bool initial = false) => new(new RepoEntry("/r"))
    {
        Status = new RepoStatus(branch, initial ? null : "abc", "origin/main", 0, 0, changes ?? []),
        Unfinished = operation is { } kind ? new OperationInfo(kind, "k", null, null, null, null, null) : null,
    };

    [Fact]
    public void The_title_says_what_runs_and_where_it_stands()
    {
        const string id = "0123456789abcdef0123456789abcdef01234567";

        Assert.Equal(TextFormat.Format(BoardText.Operation_MergeTitle, "Merge branch 'feature'"),
            OperationTools.Title(new OperationInfo(GitOperation.Merge, "k", "Merge branch 'feature'", id, null, null, null)));
        Assert.Equal(TextFormat.Format(BoardText.Operation_RebaseTitle, "feature", 2, 5, "Fix it"),
            OperationTools.Title(new OperationInfo(GitOperation.Rebase, "k", "Fix it", id, "feature", 2, 5)));
        Assert.Equal(TextFormat.Format(BoardText.Operation_RebaseTitle, "HEAD", 1, 1, "0123456"), // detached, no message
            OperationTools.Title(new OperationInfo(GitOperation.Rebase, "k", null, id, null, 1, 1)));
        Assert.Equal(TextFormat.Format(BoardText.Operation_PickTitle, SyncText.Operation_CherryPick, "0123456", "Fix it"),
            OperationTools.Title(new OperationInfo(GitOperation.CherryPick, "k", "Fix it", id, null, null, null)));
        // What git did not write (a file just being replaced) falls back to the name alone.
        Assert.Equal(TextFormat.Format(BoardText.OperationRunning, SyncText.Operation_Revert),
            OperationTools.Title(new OperationInfo(GitOperation.Revert, "k", null, null, null, null, null)));
    }

    [Fact]
    public void Continue_waits_for_every_conflict_and_a_rebase_for_every_change_in_the_index()
    {
        Assert.Equal(TextFormat.Format(SyncText.Continue_Conflicts, 1), OperationTools.ContinueBlocker(Repo([Conflict], GitOperation.Merge)));
        Assert.Null(OperationTools.ContinueBlocker(Repo([Changed, StagedOnly, New], GitOperation.Merge)));
        Assert.Equal(SyncText.Continue_Unstaged, OperationTools.ContinueBlocker(Repo([Changed], GitOperation.Rebase)));
        Assert.Null(OperationTools.ContinueBlocker(Repo([StagedOnly, New], GitOperation.Rebase)));
        Assert.Equal(SyncText.Operation_Gone, OperationTools.ContinueBlocker(Repo()));
    }

    [Fact]
    public void Only_a_merge_has_nothing_to_skip()
    {
        Assert.False(OperationTools.CanSkip(new OperationInfo(GitOperation.Merge, "k", null, null, null, null, null)));
        Assert.True(OperationTools.CanSkip(new OperationInfo(GitOperation.Rebase, "k", null, null, null, null, null)));
    }

    [Fact]
    public void Merge_and_rebase_need_a_branch_and_a_clean_worktree()
    {
        Assert.Null(OperationTools.MergeBlocker(Repo([New])));
        Assert.Equal(SyncText.Pick_Changes, OperationTools.MergeBlocker(Repo([StagedOnly])));
        Assert.Equal(SyncText.Blocker_Detached, OperationTools.MergeBlocker(Repo(branch: null)));
        Assert.Equal(SyncText.Pick_Initial, OperationTools.MergeBlocker(Repo(initial: true)));
        Assert.Equal(TextFormat.Format(SyncText.Pick_Operation, SyncText.Operation_Rebase), OperationTools.MergeBlocker(Repo(operation: GitOperation.Rebase)));
    }

    [Theory]
    [InlineData('U', 'U', nameof(BoardText.Conflict_BothModified))]
    [InlineData('A', 'A', nameof(BoardText.Conflict_BothAdded))]
    [InlineData('D', 'D', nameof(BoardText.Conflict_BothDeleted))]
    [InlineData('D', 'U', nameof(BoardText.Conflict_DeletedByUs))]
    [InlineData('U', 'D', nameof(BoardText.Conflict_DeletedByThem))]
    [InlineData('A', 'U', nameof(BoardText.Conflict_AddedByUs))]
    [InlineData('U', 'A', nameof(BoardText.Conflict_AddedByThem))]
    public void A_conflict_is_named_as_git_names_it(char index, char workTree, string key) =>
        Assert.Equal(BoardText.ResourceManager.GetString(key), OperationTools.ConflictKind(new FileChange("a", null, index, workTree, false, true)));
}
