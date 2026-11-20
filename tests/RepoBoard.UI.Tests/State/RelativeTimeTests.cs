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
    public void The_last_fetch_reads_as_its_age(int? secondsAgo, string expected) =>
        Assert.Equal(expected, RelativeTime.Text(secondsAgo is { } s ? Now.AddSeconds(-s) : null, Now));

    [Fact]
    public void A_fetch_older_than_a_day_or_none_is_stale()
    {
        Assert.True(RelativeTime.IsStale(null, Now));
        Assert.True(RelativeTime.IsStale(Now.AddDays(-1).AddSeconds(-1), Now));
        Assert.False(RelativeTime.IsStale(Now.AddHours(-23), Now));
    }
}
