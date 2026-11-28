using RepoBoard.Core.Git;
using RepoBoard.UI.State;

namespace RepoBoard.UI.Tests.State;

public sealed class RelativeTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, "nie")]
    [InlineData(30, "gerade eben")]
    [InlineData(5 * 60, "vor 5 min")]
    [InlineData(3 * 3600, "vor 3 h")]
    [InlineData(30 * 3600, "gestern")]
    [InlineData(4 * 86400, "vor 4 Tagen")]
    [InlineData(59 * 86400, "vor 59 Tagen")]
    [InlineData(60 * 86400, "vor 2 Monaten")] // 1.97 months: never "vor 1 Monaten"
    [InlineData(100 * 86400, "vor 3 Monaten")]
    [InlineData(729 * 86400, "vor 23 Monaten")]
    [InlineData(730 * 86400, "vor 2 Jahren")] // 1.999 years
    [InlineData(800 * 86400, "vor 2 Jahren")]
    public void A_time_reads_as_its_age(int? secondsAgo, string expected) =>
        Assert.Equal(expected, RelativeTime.Text(secondsAgo is { } s ? Now.AddSeconds(-s) : null, Now));

    [Theory]
    [InlineData(0, 0, false, "")]
    [InlineData(1, 0, false, "1 voraus")]
    [InlineData(2, 3, false, "2 voraus, 3 hinterher")]
    [InlineData(0, 0, true, "Upstream gelöscht")]
    public void A_branch_reads_how_it_stands_to_its_upstream(int ahead, int behind, bool gone, string expected) =>
        Assert.Equal(expected, RelativeTime.Track(new BranchInfo("b", false, false, "origin/b", ahead, behind, gone, null)));

    [Fact]
    public void A_fetch_older_than_a_day_or_none_is_stale()
    {
        Assert.True(RelativeTime.IsStale(null, Now));
        Assert.True(RelativeTime.IsStale(Now.AddDays(-1).AddSeconds(-1), Now));
        Assert.False(RelativeTime.IsStale(Now.AddHours(-23), Now));
    }
}
