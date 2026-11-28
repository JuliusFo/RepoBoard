using RepoBoard.Core.Watch;

namespace RepoBoard.Core.Tests.Watch;

/// <summary>A series of changes is read once: after a quiet moment, or at the latest after a while (WP-03).</summary>
public sealed class ChangeDebouncerTests
{
    private const long Quiet = 750;
    private const long Longest = 3000;

    [Fact]
    public void A_key_is_due_once_its_changes_paused()
    {
        var debouncer = new ChangeDebouncer<string>(Quiet, Longest);
        debouncer.Add("a", 0);
        debouncer.Add("a", 500);

        Assert.Equal(1250, debouncer.NextDue);
        Assert.Empty(debouncer.TakeDue(1249));
        Assert.Equal([("a", 500L)], debouncer.TakeDue(1250));
        Assert.Null(debouncer.NextDue);
    }

    [Fact]
    public void Changes_that_go_on_are_read_at_the_latest_after_the_longest_wait()
    {
        var debouncer = new ChangeDebouncer<string>(Quiet, Longest);
        for (var at = 0L; at <= 2900; at += 100)
        {
            debouncer.Add("build", at);
        }

        Assert.Equal(3000, debouncer.NextDue);
        Assert.Equal([("build", 2900L)], debouncer.TakeDue(3000));
    }

    [Fact]
    public void Keys_are_due_each_on_its_own()
    {
        var debouncer = new ChangeDebouncer<string>(Quiet, Longest);
        debouncer.Add("a", 0);
        debouncer.Add("b", 600);
        debouncer.Add("a", 100); // an event that arrives late does not move the last change back

        Assert.Equal([("a", 100L)], debouncer.TakeDue(900));
        Assert.Equal(1, debouncer.Count);
        Assert.Equal(1350, debouncer.NextDue);
    }

    /// <summary>
    /// Review 0.4.0, A14: a read skipped while the repository was busy comes back later but keeps the time of its change –
    /// the read at the end of the operation began after it, so the retry need not read again.
    /// </summary>
    [Fact]
    public void A_retry_waits_and_keeps_the_time_of_the_change()
    {
        var debouncer = new ChangeDebouncer<string>(Quiet, Longest);

        debouncer.Retry("busy", last: 100, notBefore: 2000);
        Assert.Equal(2000, debouncer.NextDue);
        Assert.Empty(debouncer.TakeDue(1999));

        debouncer.Add("busy", 1500); // a change meanwhile: its own quiet time, not before the retry
        Assert.Equal(2250, debouncer.NextDue);
        Assert.Equal([("busy", 1500L)], debouncer.TakeDue(2250));
    }
}
