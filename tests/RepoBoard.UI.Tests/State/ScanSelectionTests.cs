using RepoBoard.Core.Repos;
using RepoBoard.UI.State;

namespace RepoBoard.UI.Tests.State;

/// <summary>Which repositories of a scan are ticked (review 0.4.0, A9).</summary>
public sealed class ScanSelectionTests
{
    private static readonly ScanCandidate Plain = new("/s/plain", "/s/plain", false, []);
    private static readonly ScanCandidate Hooked = new("/s/hooked", "/s/hooked", false, [RepoRuns.Hooks]);
    private static readonly ScanCandidate Outside = new("/s/wt", "/other/main", true, []);

    [Fact]
    public void Only_a_repository_that_runs_nothing_of_its_own_inside_the_folder_is_ticked_at_first()
    {
        var selection = new ScanSelection([Hooked, Plain, Outside]);

        Assert.Equal([Plain], selection.Selected);
    }

    [Fact]
    public void The_user_ticks_and_unticks_and_the_list_keeps_its_order()
    {
        var selection = new ScanSelection([Hooked, Plain, Outside]);

        selection.Toggle(Outside);
        selection.Toggle(Hooked);
        selection.Toggle(Plain);

        Assert.Equal([Hooked, Outside], selection.Selected);
        Assert.False(selection.IsSelected(Plain));
    }

    [Fact]
    public void Each_kind_of_own_program_has_its_own_text() =>
        Assert.Equal(Enum.GetValues<RepoRuns>().Length, Enum.GetValues<RepoRuns>().Select(ScanSelection.Text).Distinct().Count());
}
