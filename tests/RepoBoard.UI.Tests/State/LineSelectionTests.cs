using RepoBoard.Core.Git;
using RepoBoard.UI.State;

namespace RepoBoard.UI.Tests.State;

public sealed class LineSelectionTests
{
    // Hunk 0: context, -b, -c, +B, +C, context. Hunk 1: -x, +X.
    private static readonly FileDiff Diff = FileDiff.Parse(
        "@@ -1,4 +1,4 @@\n a\n-b\n-c\n+B\n+C\n d\n@@ -20 +20 @@\n-x\n+X\n");

    [Fact]
    public void A_click_picks_and_drops_a_changed_line_but_not_context()
    {
        var selection = new LineSelection();

        selection.Click(Diff.Hunks[0], 0, 1, shift: false);
        selection.Click(Diff.Hunks[0], 0, 0, shift: false);
        Assert.Equal([1], selection.Lines);

        selection.Click(Diff.Hunks[0], 0, 1, shift: false);
        Assert.Null(selection.Hunk);
    }

    [Fact]
    public void Shift_click_picks_the_changed_lines_of_the_range()
    {
        var selection = new LineSelection();

        selection.Click(Diff.Hunks[0], 0, 4, shift: false);
        selection.Click(Diff.Hunks[0], 0, 0, shift: true);

        Assert.Equal([1, 2, 3, 4], selection.Lines);
        Assert.Equal(4, selection.CountIn(0));
    }

    [Fact]
    public void A_click_in_another_hunk_starts_over()
    {
        var selection = new LineSelection();
        selection.Click(Diff.Hunks[0], 0, 1, shift: false);

        selection.Click(Diff.Hunks[1], 1, 1, shift: true);

        Assert.Equal(1, selection.Hunk);
        Assert.Equal([1], selection.Lines);
        Assert.False(selection.IsPicked(0, 1));
    }

    [Fact]
    public void The_picks_stay_only_while_their_hunk_reads_the_same()
    {
        var selection = new LineSelection();
        selection.Click(Diff.Hunks[1], 1, 0, shift: false);

        // The first hunk changed, the picked one did not.
        selection.KeepFor(Diff, FileDiff.Parse("@@ -1,3 +1,3 @@\n a\n-b\n+B\n d\n@@ -20 +20 @@\n-x\n+X\n"));
        Assert.True(selection.IsPicked(1, 0));

        selection.KeepFor(Diff, FileDiff.Parse("@@ -20 +20 @@\n-x\n+Y\n"));
        Assert.Null(selection.Hunk);
    }
}
