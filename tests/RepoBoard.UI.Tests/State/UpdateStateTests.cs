using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RepoBoard.Core.Board;
using RepoBoard.Core.Git;
using RepoBoard.Core.Repos;
using RepoBoard.Core.Resources;
using RepoBoard.UI.Resources;
using RepoBoard.UI.State;

namespace RepoBoard.UI.Tests.State;

/// <summary>WP-10 (FerretSharp WP-32): the app only reports updates; downloading and installing wait for the click, the restart ends the app first.</summary>
public sealed class UpdateStateTests : IDisposable
{
    private static readonly AppUpdate Next = AppUpdate.From(new VersionNotes("0.2.0", "### Added\n- Updates from the app"));

    /// <summary>Released after <see cref="Next"/> was reported.</summary>
    private static readonly AppUpdate Later = AppUpdate.From(new VersionNotes("0.3.0", "### Fixed\n- Updates take the newest version"),
        [new VersionNotes("0.2.0", "### Added\n- Updates from the app")]);

    private readonly TestFolder _folder = new();
    private readonly FakeUpdater _updater = new();
    private readonly AppExit _exit = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 20, 9, 30, 0, TimeSpan.Zero));
    private readonly BoardService _board;

    public UpdateStateTests() =>
        _board = Boards.New(_folder.Path, _time);

    public void Dispose() => _folder.Dispose();

    private UpdateState Create() => new(_updater, _board, _exit, NullLogger<UpdateState>.Instance, _time);

    private Task SettingsAsync(Func<AppSettings, AppSettings> change) => _board.UpdateSettingsAsync(change, TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_copy_that_was_not_installed_never_checks()
    {
        _updater.IsInstalled = false;
        var updates = Create();

        await updates.StartAsync();
        await updates.CheckAsync();

        Assert.Equal(UpdatePhase.NotInstalled, updates.Phase);
        Assert.False(updates.CanCheck);
        Assert.Equal(0, _updater.Checks);
    }

    [Fact]
    public async Task The_check_at_start_reports_a_newer_version_with_its_notes_and_downloads_nothing()
    {
        _updater.Found = Next;
        var updates = Create();

        await updates.StartAsync();

        Assert.Equal(UpdatePhase.Available, updates.Phase);
        Assert.Equal(Next, updates.Update);
        Assert.Equal(_time.GetLocalNow(), updates.LastChecked);
        Assert.Empty(_updater.Downloads);
    }

    [Fact]
    public async Task With_the_check_at_start_off_only_check_now_asks()
    {
        await SettingsAsync(s => s with { CheckForUpdates = false });
        var updates = Create();

        await updates.StartAsync();
        Assert.Equal(UpdatePhase.Idle, updates.Phase);
        Assert.Equal(0, _updater.Checks);

        await updates.CheckAsync();

        Assert.Equal(UpdatePhase.UpToDate, updates.Phase);
        Assert.Null(updates.Update);
        Assert.Equal(1, _updater.Checks);
    }

    [Fact]
    public async Task A_failed_check_is_a_note_and_can_be_tried_again()
    {
        _updater.CheckFails = new HttpRequestException("No such host is known.");
        var updates = Create();

        await updates.StartAsync();

        Assert.Equal(UpdatePhase.CheckFailed, updates.Phase);
        Assert.Equal("No such host is known.", updates.Error);
        Assert.True(updates.CanCheck);

        _updater.CheckFails = null;
        _updater.Found = Next;
        await updates.CheckAsync();

        Assert.Equal(UpdatePhase.Available, updates.Phase);
        Assert.Null(updates.Error);
    }

    [Fact]
    public async Task A_check_without_answer_gives_up()
    {
        _updater.CheckHangs = true;
        var updates = Create();

        var check = updates.CheckAsync();
        Assert.Equal(UpdatePhase.Checking, updates.Phase);
        Assert.False(updates.CanCheck);
        _time.Advance(UpdateState.CheckTimeout);
        await check;

        Assert.Equal(UpdatePhase.CheckFailed, updates.Phase);
        Assert.Equal(TextFormat.Format(UpdateText.CheckTimedOut, 30), updates.Error); // the limit from the code, not a second copy in the text
    }

    [Fact]
    public async Task A_download_left_from_the_last_session_waits_for_the_restart_without_checking()
    {
        _updater.Downloaded = Next;
        var updates = Create();

        await updates.StartAsync();

        Assert.Equal(UpdatePhase.Ready, updates.Phase);
        Assert.Equal(Next, updates.Update);
        Assert.False(updates.CanCheck);
        Assert.Equal(0, _updater.Checks);
    }

    [Fact]
    public async Task Download_waits_for_restart_now_which_ends_the_app_and_installs_only_once_it_ends()
    {
        _updater.Found = Next;
        _updater.Progress = [10, 55, 100];
        var updates = Create();
        var progress = new List<int>();
        updates.Changed += () => progress.Add(updates.Progress);
        var closeRequests = 0;
        _exit.Requested += () => closeRequests++;
        await updates.CheckAsync();
        updates.ShowDetails();

        await updates.DownloadAsync();

        Assert.Equal([Next], _updater.Downloads);
        Assert.Contains(55, progress);
        Assert.Equal(UpdatePhase.Ready, updates.Phase);
        Assert.True(updates.DetailsOpen); // the second click is the user's (FerretSharp WP-32): nothing closes after the download
        Assert.Equal(0, closeRequests);

        updates.Restart();

        Assert.False(updates.DetailsOpen);
        Assert.Equal(1, closeRequests);
        Assert.Empty(_updater.Applied);

        _exit.Exiting();
        _exit.Exiting(); // the host calls it once per end; a second call must not start the updater twice

        Assert.Equal([Next], _updater.Applied);
    }

    [Fact]
    public void An_exit_from_the_tray_installs_nothing()
    {
        var updates = Create();

        _exit.Exiting();

        Assert.Equal(UpdatePhase.Idle, updates.Phase);
        Assert.Empty(_updater.Applied);
    }

    [Fact]
    public async Task A_canceled_download_leaves_the_update_available()
    {
        _updater.Found = Next;
        _updater.DownloadHangs = true;
        var updates = Create();
        var closeRequests = 0;
        _exit.Requested += () => closeRequests++;
        await updates.CheckAsync();

        var download = updates.DownloadAsync();
        Assert.Equal(UpdatePhase.Downloading, updates.Phase);
        updates.CancelDownload();
        await download;

        Assert.Equal(UpdatePhase.Available, updates.Phase);
        Assert.Equal(0, closeRequests);
    }

    [Fact]
    public async Task A_failed_download_says_why_and_can_be_tried_again()
    {
        _updater.Found = Next;
        _updater.DownloadFails = new IOException("Disk full");
        var updates = Create();
        await updates.CheckAsync();

        await updates.DownloadAsync();

        Assert.Equal(UpdatePhase.DownloadFailed, updates.Phase);
        Assert.Equal("Disk full", updates.Error);

        _updater.DownloadFails = null;
        await updates.DownloadAsync();

        Assert.Equal(UpdatePhase.Ready, updates.Phase);
        Assert.Equal(2, _updater.Downloads.Count);
    }

    [Fact]
    public async Task An_updater_that_cannot_start_does_not_stop_the_app_from_ending()
    {
        _updater.Downloaded = Next;
        _updater.ApplyFails = true;
        var updates = Create();
        await updates.StartAsync();

        updates.Restart();
        _exit.Exiting();

        Assert.Equal([Next], _updater.Applied);
    }

    [Fact]
    public async Task While_running_it_checks_again_once_the_interval_since_the_last_check_has_passed()
    {
        using var updates = Create(); // 4 hours by default
        await updates.StartAsync();
        Assert.Equal((UpdatePhase.UpToDate, 1), (updates.Phase, _updater.Checks));

        _time.Advance(TimeSpan.FromHours(4) - UpdateState.BackgroundTick);
        Assert.Equal(1, _updater.Checks);

        _updater.Found = Next;
        _time.Advance(UpdateState.BackgroundTick);
        Assert.Equal((UpdatePhase.Available, Next, 2), (updates.Phase, updates.Update, _updater.Checks));

        _time.Advance(TimeSpan.FromHours(4));
        Assert.Equal((UpdatePhase.Available, Next, 3), (updates.Phase, updates.Update, _updater.Checks)); // silently on
    }

    [Fact]
    public async Task While_an_update_is_available_a_silent_check_replaces_it_with_a_newer_one()
    {
        _updater.Found = Next;
        using var updates = Create();
        await updates.StartAsync();
        var phases = new List<UpdatePhase>();
        updates.Changed += () => phases.Add(updates.Phase);

        _updater.Found = Later;
        _time.Advance(TimeSpan.FromHours(4));

        Assert.Equal((UpdatePhase.Available, Later, 2), (updates.Phase, updates.Update, _updater.Checks));
        Assert.DoesNotContain(UpdatePhase.Checking, phases); // the top bar keeps showing the update
    }

    [Fact]
    public async Task While_an_update_is_available_a_failed_silent_check_changes_nothing()
    {
        _updater.Found = Next;
        using var updates = Create();
        await updates.StartAsync();
        var changes = 0;
        updates.Changed += () => changes++;

        _updater.CheckFails = new HttpRequestException("No such host is known.");
        _time.Advance(TimeSpan.FromHours(4));

        Assert.Equal((UpdatePhase.Available, Next, null, 2), (updates.Phase, updates.Update, updates.Error, _updater.Checks));
        Assert.Equal(0, changes);
        Assert.Equal(_time.GetLocalNow(), updates.LastChecked); // counts as a check: the next one in 4 hours
    }

    [Fact]
    public async Task While_the_dialog_is_open_it_does_not_check()
    {
        _updater.Found = Next;
        using var updates = Create();
        await updates.StartAsync();
        updates.ShowDetails();

        _updater.Found = Later;
        _time.Advance(TimeSpan.FromDays(1));

        Assert.Equal((Next, 1), (updates.Update, _updater.Checks)); // the notes being read stay as they are
    }

    [Fact]
    public async Task A_failed_check_now_keeps_the_update_known_before()
    {
        _updater.Found = Next;
        var updates = Create();
        await updates.CheckAsync();

        _updater.CheckFails = new HttpRequestException("No such host is known.");
        await updates.CheckAsync();

        Assert.Equal((UpdatePhase.Available, Next, null), (updates.Phase, updates.Update, updates.Error));
    }

    [Fact]
    public async Task A_failed_check_now_after_a_failed_download_keeps_both()
    {
        _updater.Found = Next;
        _updater.DownloadFails = new IOException("Disk full");
        var updates = Create();
        await updates.CheckAsync();
        await updates.DownloadAsync();

        _updater.CheckFails = new HttpRequestException("No such host is known.");
        await updates.CheckAsync();

        Assert.Equal((UpdatePhase.DownloadFailed, Next, "Disk full"), (updates.Phase, updates.Update, updates.Error));
    }

    [Fact]
    public async Task Download_checks_again_and_takes_a_version_released_since_the_last_check()
    {
        _updater.Found = Next;
        var updates = Create();
        await updates.CheckAsync();
        updates.ShowDetails();

        _updater.Found = Later;
        await updates.DownloadAsync();

        Assert.Equal([Later], _updater.Downloads);
        Assert.Equal((UpdatePhase.Ready, Later, true), (updates.Phase, updates.Update, updates.DetailsOpen));
        Assert.Equal(2, _updater.Checks);
    }

    /// <summary>The check right before the download fails or gets no answer: the version known from before is downloaded (one theory, review 0.4.0, C15).</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Download_takes_the_known_version_if_the_check_before_fails(bool noAnswer)
    {
        _updater.Found = Next;
        var updates = Create();
        await updates.CheckAsync();

        if (noAnswer)
        {
            _updater.CheckHangs = true;
        }
        else
        {
            _updater.CheckFails = new HttpRequestException("No such host is known.");
        }

        var download = updates.DownloadAsync();
        if (noAnswer)
        {
            Assert.Equal(UpdatePhase.Downloading, updates.Phase);
            _time.Advance(UpdateState.CheckTimeout);
        }

        await download;

        Assert.Equal([Next], _updater.Downloads);
        Assert.Equal((UpdatePhase.Ready, null), (updates.Phase, updates.Error));
    }

    [Fact]
    public async Task Download_canceled_during_the_check_before_downloads_nothing()
    {
        _updater.Found = Next;
        var updates = Create();
        await updates.CheckAsync();

        _updater.CheckHangs = true;
        var download = updates.DownloadAsync();
        updates.CancelDownload();
        await download;

        Assert.Empty(_updater.Downloads);
        Assert.Equal((UpdatePhase.Available, Next), (updates.Phase, updates.Update));
    }

    [Fact]
    public async Task Download_of_a_release_taken_back_since_the_check_downloads_nothing_and_closes_the_dialog()
    {
        _updater.Found = Next;
        var updates = Create();
        await updates.CheckAsync();
        updates.ShowDetails();

        _updater.Found = null;
        await updates.DownloadAsync();

        Assert.Empty(_updater.Downloads);
        Assert.Equal((UpdatePhase.UpToDate, null, false), (updates.Phase, updates.Update, updates.DetailsOpen));
    }

    /// <summary>
    /// Review WP-10: started with Windows, the network is often not there yet. A failed check is tried again after 15 min
    /// (also with "never" – it stands for the check at start), until one answers; then the interval counts again.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(24)]
    public async Task While_running_a_failed_check_is_tried_again_after_15_minutes_until_one_answers(int hours)
    {
        await SettingsAsync(s => s with { UpdateCheckHours = hours });
        _updater.CheckFails = new HttpRequestException("No such host is known.");
        using var updates = Create();
        await updates.StartAsync();

        _time.Advance(UpdateState.RetryAfterFailure - UpdateState.BackgroundTick);
        Assert.Equal((UpdatePhase.CheckFailed, 1), (updates.Phase, _updater.Checks));

        _time.Advance(UpdateState.BackgroundTick);
        Assert.Equal((UpdatePhase.CheckFailed, 2), (updates.Phase, _updater.Checks)); // a failed retry counts as a check

        _updater.CheckFails = null;
        _time.Advance(UpdateState.RetryAfterFailure);
        Assert.Equal((UpdatePhase.UpToDate, 3), (updates.Phase, _updater.Checks));

        _time.Advance(TimeSpan.FromHours(23));
        Assert.Equal(3, _updater.Checks); // answered: back to the interval (or none with "never")
    }

    [Fact]
    public async Task A_failed_check_now_with_automatic_checks_off_is_not_tried_again()
    {
        await SettingsAsync(s => s with { CheckForUpdates = false });
        _updater.CheckFails = new HttpRequestException("No such host is known.");
        using var updates = Create();
        await updates.StartAsync();
        await updates.CheckAsync();

        _time.Advance(TimeSpan.FromHours(2));

        Assert.Equal((UpdatePhase.CheckFailed, 1), (updates.Phase, _updater.Checks));
    }

    /// <summary>Review WP-10: a download that cannot be read must not end the start of the app.</summary>
    [Fact]
    public async Task A_download_that_cannot_be_read_is_none_and_the_check_goes_on()
    {
        _updater.DownloadedFails = new UnauthorizedAccessException("packages is locked");
        _updater.Found = Next;
        var updates = Create();

        await updates.StartAsync();

        Assert.Equal((UpdatePhase.Available, Next, 1), (updates.Phase, updates.Update, _updater.Checks));
    }

    [Fact]
    public async Task While_running_it_does_not_check_with_automatic_checks_off_or_never()
    {
        await SettingsAsync(s => s with { CheckForUpdates = false });
        using var updates = Create();
        await updates.StartAsync();

        _time.Advance(TimeSpan.FromDays(2));
        Assert.Equal((UpdatePhase.Idle, 0), (updates.Phase, _updater.Checks));

        await SettingsAsync(s => s with { CheckForUpdates = true, UpdateCheckHours = 0 });
        _time.Advance(TimeSpan.FromDays(2));
        Assert.Equal(0, _updater.Checks);

        await SettingsAsync(s => s with { UpdateCheckHours = 24 });
        _time.Advance(UpdateState.BackgroundTick);
        Assert.Equal(1, _updater.Checks); // due since the start long ago
    }

    [Fact]
    public void The_dialog_opens_only_with_an_update()
    {
        var updates = Create();

        updates.ShowDetails();

        Assert.False(updates.DetailsOpen);
    }

    /// <summary>WP-10: RepoBoard often runs in the notification area only; a newer version is announced once, across starts.</summary>
    [Fact]
    public async Task Each_newer_version_is_announced_once_across_starts()
    {
        _updater.Found = Next;
        var announced = new List<string>();
        using (var first = Create())
        {
            first.Announced += u => announced.Add(u.Version);
            await first.StartAsync();
            await first.CheckAsync();
        }

        using var second = Create(); // the next start (with Windows, every morning)
        second.Announced += u => announced.Add(u.Version);
        await second.StartAsync();
        Assert.Equal(["0.2.0"], announced);
        Assert.Equal("0.2.0", _board.Settings.UpdateNotified);

        _updater.Found = Later;
        await second.CheckAsync();

        Assert.Equal(["0.2.0", "0.3.0"], announced);
        Assert.Equal("0.3.0", _board.Settings.UpdateNotified);
    }

    [Fact]
    public async Task An_older_version_than_one_announced_is_not_announced()
    {
        await SettingsAsync(s => s with { UpdateNotified = "0.3.0" }); // announced, then taken back
        _updater.Found = Next;
        var announced = 0;
        var updates = Create();
        updates.Announced += _ => announced++;

        await updates.StartAsync();

        Assert.Equal((UpdatePhase.Available, 0), (updates.Phase, announced)); // the top bar shows it all the same
        Assert.Equal("0.3.0", _board.Settings.UpdateNotified);
    }

    private sealed class FakeUpdater : IAppUpdater
    {
        public bool IsInstalled { get; set; } = true;

        public AppUpdate? Downloaded
        {
            get => DownloadedFails is { } fails ? throw fails : field;
            set;
        }

        public Exception? DownloadedFails { get; set; }

        public AppUpdate? Found { get; set; }

        public Exception? CheckFails { get; set; }

        public bool CheckHangs { get; set; }

        public int Checks { get; private set; }

        public int[] Progress { get; set; } = [100];

        public Exception? DownloadFails { get; set; }

        public bool DownloadHangs { get; set; }

        public List<AppUpdate> Downloads { get; } = [];

        public bool ApplyFails { get; set; }

        public List<AppUpdate> Applied { get; } = [];

        public async Task<AppUpdate?> CheckAsync(CancellationToken cancellationToken)
        {
            Checks++;
            if (CheckHangs)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return CheckFails is { } fails ? throw fails : Found;
        }

        public async Task DownloadAsync(AppUpdate update, Action<int> progress, CancellationToken cancellationToken)
        {
            Downloads.Add(update);
            if (DownloadHangs)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (DownloadFails is { } fails)
            {
                throw fails;
            }

            foreach (var percent in Progress)
            {
                progress(percent);
            }
        }

        public void ApplyAfterExit(AppUpdate update)
        {
            Applied.Add(update);
            if (ApplyFails)
            {
                throw new InvalidOperationException("Update.exe not found");
            }
        }
    }
}
