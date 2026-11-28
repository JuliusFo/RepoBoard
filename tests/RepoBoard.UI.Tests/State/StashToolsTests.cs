using RepoBoard.Core.Git;
using RepoBoard.UI.Resources;
using RepoBoard.UI.State;

namespace RepoBoard.UI.Tests.State;

/// <summary>The question before a discard says where the changes go (WP-06, ADR 0016).</summary>
public sealed class StashToolsTests
{
    private static readonly FileChange Changed = new("a.txt", null, '.', 'M', false, false);
    private static readonly FileChange New = new("c.txt", null, '.', '?', true, false);

    public static TheoryData<FileChange[], string> Cases => new()
    {
        { [Changed], BoardText.Discard_TextStash },
        { [New], BoardText.Discard_TextRecycle },
        { [Changed, New], BoardText.Discard_TextBoth },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_question_names_the_stash_list_the_recycle_bin_or_both(FileChange[] files, string expected) =>
        Assert.Equal(expected, StashTools.DiscardText(files));
}
