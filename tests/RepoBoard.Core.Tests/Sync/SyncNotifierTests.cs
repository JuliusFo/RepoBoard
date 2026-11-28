using Microsoft.Extensions.Time.Testing;
using RepoBoard.Core.Board;
using RepoBoard.Core.Git;
using RepoBoard.Core.Repos;
using RepoBoard.Core.Sync;

namespace RepoBoard.Core.Tests.Sync;

/// <summary>Which auto-sync entries become a Windows notification, and how several become one (ADR 0006).</summary>
public sealed class SyncNotifierTests : IDisposable
{
    private readonly TestFolder _folder = new();
    private readonly FakeTimeProvider _time = new();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(ActivityKind.Pulled, true, NotificationLevel.PullsAndProblems, true)]
    [InlineData(ActivityKind.Error, true, NotificationLevel.PullsAndProblems, true)]
    [InlineData(ActivityKind.Skipped, true, NotificationLevel.PullsAndProblems, true)]
    [InlineData(ActivityKind.NewCommits, true, NotificationLevel.PullsAndProblems, false)] // auto-fetch alone: the board shows it
    [InlineData(ActivityKind.Info, true, NotificationLevel.PullsAndProblems, false)]
    [InlineData(ActivityKind.Pulled, true, NotificationLevel.Problems, false)]
    [InlineData(ActivityKind.Error, true, NotificationLevel.Problems, true)]
    [InlineData(ActivityKind.Skipped, true, NotificationLevel.Problems, true)]
    [InlineData(ActivityKind.Error, true, NotificationLevel.Off, false)]
    [InlineData(ActivityKind.Error, false, NotificationLevel.PullsAndProblems, false)] // a click: the user is looking
    [InlineData(ActivityKind.Pulled, false, NotificationLevel.PullsAndProblems, false)]
    public void Only_auto_sync_entries_the_setting_asks_for(ActivityKind kind, bool auto, NotificationLevel level, bool expected) =>
        Assert.Equal(expected, SyncNotifier.Wanted(Entry("a", kind, auto), level));

    [Fact]
    public void One_repository_is_the_title_and_a_click_opens_it()
    {
        var notification = SyncNotifier.Compose([Entry("api", ActivityKind.Pulled)]);

        Assert.Equal(new SyncNotification("api", "Pulled", ActivityKind.Pulled, Path("api")), notification);
    }

    [Fact]
    public void Several_repositories_are_counted_with_problems_first_and_no_repository_to_open()
    {
        using var _ = UiCulture.Use("en");
        var notification = SyncNotifier.Compose(
            [Entry("a", ActivityKind.Pulled), Entry("b", ActivityKind.Skipped), Entry("c", ActivityKind.Error), Entry("d", ActivityKind.Pulled), Entry("e", ActivityKind.Pulled)]);

        Assert.Equal("Auto-sync: 5 repositories", notification.Title);
        Assert.Equal("c: Error\nb: Skipped\na: Pulled\nd: Pulled\n… and 1 more", notification.Text);
        Assert.Equal(ActivityKind.Error, notification.Worst);
        Assert.Null(notification.RepoPath);
    }

    [Fact]
    public void Title_and_text_fit_what_Windows_shows()
    {
        var notification = SyncNotifier.Compose([new ActivityEntry(default, new string('n', 100), new string('t', 400), ActivityKind.Error, true, Path("n"))]);

        Assert.Equal(SyncNotifier.TitleLimit, notification.Title.Length);
        Assert.Equal(SyncNotifier.TextLimit, notification.Text.Length);
        Assert.EndsWith("…", notification.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task What_one_tick_logs_comes_as_one_notification_after_the_gathering_time()
    {
        var board = await NewBoardAsync(NotificationLevel.PullsAndProblems);
        using var notifier = new SyncNotifier(board, _time);
        var shown = new List<SyncNotification>();
        notifier.Show += shown.Add;

        board.Activity.Add(Entry("a", ActivityKind.Pulled));
        board.Activity.Add(Entry("b", ActivityKind.Error));
        board.Activity.Add(Entry("c", ActivityKind.Info)); // not asked for
        _time.Advance(SyncNotifier.Gather - TimeSpan.FromMilliseconds(1));
        Assert.Empty(shown);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        var notification = Assert.Single(shown);
        Assert.Equal(ActivityKind.Error, notification.Worst);
        Assert.DoesNotContain("Info", notification.Text, StringComparison.Ordinal);

        board.Activity.Add(Entry("d", ActivityKind.Pulled)); // a later tick: a new notification
        _time.Advance(SyncNotifier.Gather);
        Assert.Equal(2, shown.Count);
    }

    [Fact]
    public async Task Switched_off_nothing_is_shown()
    {
        var board = await NewBoardAsync(NotificationLevel.Off);
        using var notifier = new SyncNotifier(board, _time);
        var shown = 0;
        notifier.Show += _ => shown++;

        board.Activity.Add(Entry("a", ActivityKind.Error));
        _time.Advance(SyncNotifier.Gather);

        Assert.Equal(0, shown);
    }

    private async Task<BoardService> NewBoardAsync(NotificationLevel level)
    {
        var board = Boards.New(_folder.Path, _time);
        await board.UpdateSettingsAsync(s => s with { Notifications = level }, Ct);
        return board;
    }

    private string Path(string repo) => _folder.Combine(repo);

    private ActivityEntry Entry(string repo, ActivityKind kind, bool auto = true) => new(default, repo, kind.ToString(), kind, auto, Path(repo));

    public void Dispose() => _folder.Dispose();
}
