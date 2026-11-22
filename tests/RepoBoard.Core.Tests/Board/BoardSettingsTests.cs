using Microsoft.Extensions.Time.Testing;
using RepoBoard.Core.Board;
using RepoBoard.Core.Git;
using RepoBoard.Core.Repos;

namespace RepoBoard.Core.Tests.Board;

/// <summary>The settings beside the board: changed from the page and from the update check on the thread pool.</summary>
public sealed class BoardSettingsTests : IDisposable
{
    private readonly TestFolder _folder = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Dispose();

    /// <summary>
    /// CLAUDE.md §2: a config of a newer RepoBoard stays as it is, whatever the user switches – without the board's check the
    /// first click wrote over it (review 0.4.0, C3).
    /// </summary>
    [Fact]
    public async Task A_config_of_a_newer_version_is_never_saved_over()
    {
        var time = new FakeTimeProvider();
        var file = _folder.Combine("config.json");
        const string newer = "{ \"version\": 99, \"repos\": [ { \"path\": \"/somewhere\", \"newThing\": 1 } ] }";
        await File.WriteAllTextAsync(file, newer, Ct);
        var board = Boards.New(_folder.Path, time);
        await board.LoadAsync(Ct);

        await board.UpdateSettingsAsync(s => s with { CloseToTray = !s.CloseToTray }, Ct);
        await board.SetAutoSyncMinutesAsync(15, Ct);

        Assert.Equal(newer, await File.ReadAllTextAsync(file, Ct));
        Assert.Equal(15, board.AutoSyncMinutes); // holds until the app ends
    }

    /// <summary>
    /// Review WP-10: the update check saves the announced version from the thread pool while the user switches a setting.
    /// The second change must see the first, or the first is lost.
    /// </summary>
    [Fact]
    public async Task Two_changes_at_once_both_stay()
    {
        var time = new FakeTimeProvider();
        var board = Boards.New(_folder.Path, time);
        using var firstReading = new ManualResetEventSlim();
        using var firstMayFinish = new ManualResetEventSlim();

        var first = Task.Run(() => board.UpdateSettingsAsync(s =>
        {
            firstReading.Set();
            firstMayFinish.Wait(TimeSpan.FromSeconds(10), Ct);
            return s with { UpdateNotified = "0.2.0" };
        }, Ct), Ct);
        Assert.True(firstReading.Wait(TimeSpan.FromSeconds(10), Ct));
        var second = Task.Run(() => board.UpdateSettingsAsync(s => s with { CloseToTray = false }, Ct), Ct);
        // The second change waits for the first: without the lock it ran past it and was done at once. A slow machine can
        // only make this wait longer, never green without the lock (review 0.4.0, C6: a fixed 100 ms could pass unlocked).
        await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(1), Ct));
        Assert.False(second.IsCompleted, "the second change did not wait for the first");
        firstMayFinish.Set();
        await Task.WhenAll(first, second);

        Assert.Equal(("0.2.0", false), (board.Settings.UpdateNotified, board.Settings.CloseToTray));
    }
}
