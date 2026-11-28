using RepoBoard.Core.Board;
using RepoBoard.Core.Git;
using RepoBoard.Core.Resources;
using RepoBoard.Core.Sync;

namespace RepoBoard.Core.Tests.Board;

/// <summary>When discarding and stashing keep out (WP-06, ADR 0016) – the rules the buttons and the gate share.</summary>
public sealed class StashCheckTests
{
    private static readonly FileChange Changed = new("a.txt", null, '.', 'M', false, false);
    private static readonly FileChange New = new("c.txt", null, '.', '?', true, false);
    private static readonly FileChange Conflicted = new("x.txt", null, 'U', 'U', false, true);

    private static RepoStatus Status(bool initial, params FileChange[] changes) =>
        new("main", initial ? null : "abc", null, 0, 0, changes);

    public static TheoryData<RepoStatus, string, string?> DiscardCases => new()
    {
        { Status(false, Changed, New), "a.txt", null },
        // A tracked file goes into a stash entry: git keeps one only on a commit, and not while a conflict is open.
        { Status(true, Changed), "a.txt", SyncText.Pick_Initial },
        { Status(false, Changed, Conflicted), "a.txt", SyncText.Stash_Conflicts },
        // A new file alone goes to the recycle bin: neither keeps it out.
        { Status(true, New, Conflicted), "c.txt", null },
    };

    [Theory]
    [MemberData(nameof(DiscardCases))]
    public void Discard_keeps_out_only_where_the_stash_entry_cannot_be_written(RepoStatus status, string file, string? expected) =>
        Assert.Equal(expected, StashCheck.Discard(status, [file]));

    public static TheoryData<RepoStatus, GitOperation?, bool, string?> CreateCases => new()
    {
        { Status(false, Changed), null, false, null },
        { Status(false, New), null, true, null },
        { Status(false, New), null, false, SyncText.Stash_OnlyNew },
        { Status(false), null, true, SyncText.Stash_Nothing },
        { Status(true, New), null, true, SyncText.Pick_Initial },
        { Status(false, Changed), GitOperation.Rebase, false, TextFormat.Format(SyncText.Pick_Operation, PullCheck.Name(GitOperation.Rebase)) },
        { Status(false, Changed, Conflicted), null, false, SyncText.Stash_Conflicts },
    };

    [Theory]
    [MemberData(nameof(CreateCases))]
    public void Stash_keeps_out_with_a_reason(RepoStatus status, GitOperation? running, bool includeUntracked, string? expected) =>
        Assert.Equal(expected, StashCheck.Create(status, running, includeUntracked));

    [Fact]
    public void Discard_offers_neither_a_conflict_nor_a_nested_repository()
    {
        Assert.True(StashCheck.CanDiscard(Changed));
        Assert.True(StashCheck.CanDiscard(New));
        Assert.False(StashCheck.CanDiscard(Conflicted));
        Assert.False(StashCheck.CanDiscard(new FileChange("sub/", null, '.', '?', true, false)));
        Assert.False(StashCheck.CanDiscard(new FileChange("s.txt", null, 'M', '.', false, false))); // only staged
    }
}
