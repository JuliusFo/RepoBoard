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
        await Task.Delay(100, Ct); // the second change runs into the first
        firstMayFinish.Set();
        await Task.WhenAll(first, second);

        Assert.Equal(("0.2.0", false), (board.Settings.UpdateNotified, board.Settings.CloseToTray));
    }
}
