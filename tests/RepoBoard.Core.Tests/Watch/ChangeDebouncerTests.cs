using RepoBoard.Core.Watch;

namespace RepoBoard.Core.Tests.Watch;

/// <summary>A series of changes is read once: after a quiet moment, or at the latest after a while (WP-03).</summary>
public sealed class ChangeDebouncerTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 21, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan Longest = TimeSpan.FromSeconds(3);

    private static DateTimeOffset At(int ms) => Start + TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void A_key_is_due_once_its_changes_paused()
    {
        var debouncer = new ChangeDebouncer<string>(Quiet, Longest);
        debouncer.Add("a", At(0));
        debouncer.Add("a", At(500));

        Assert.Equal(At(1250), debouncer.NextDue);
        Assert.Empty(debouncer.TakeDue(At(1249)));
        Assert.Equal([("a", At(500))], debouncer.TakeDue(At(1250)));
        Assert.Null(debouncer.NextDue);
    }

    [Fact]
    public void Changes_that_go_on_are_read_at_the_latest_after_the_longest_wait()
    {
        var debouncer = new ChangeDebouncer<string>(Quiet, Longest);
        for (var ms = 0; ms <= 2900; ms += 100)
        {
            debouncer.Add("build", At(ms));
        }

        Assert.Equal(At(3000), debouncer.NextDue);
        Assert.Equal([("build", At(2900))], debouncer.TakeDue(At(3000)));
    }

    [Fact]
    public void Keys_are_due_each_on_its_own()
    {
        var debouncer = new ChangeDebouncer<string>(Quiet, Longest);
        debouncer.Add("a", At(0));
        debouncer.Add("b", At(600));
        debouncer.Add("a", At(100)); // an event that arrives late does not move the last change back

        Assert.Equal([("a", At(100))], debouncer.TakeDue(At(900)));
        Assert.Equal(1, debouncer.Count);
        Assert.Equal(At(1350), debouncer.NextDue);
    }
}
