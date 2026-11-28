using RepoBoard.Core.Repos;
using RepoBoard.UI.State;

namespace RepoBoard.UI.Tests.State;

/// <summary>Each interval of the update check reads as itself (review 0.4.0, B8).</summary>
public sealed class UpdateIntervalsTests
{
    [Fact]
    public void Every_offered_interval_has_a_label_of_its_own() =>
        Assert.Equal(AppSettings.UpdateCheckIntervals.Length, AppSettings.UpdateCheckIntervals.Select(UpdateIntervals.Label).Distinct().Count());

    [Fact]
    public void An_interval_without_a_name_says_its_hours() => Assert.Equal("Alle 8 Stunden", UpdateIntervals.Label(8));
}
