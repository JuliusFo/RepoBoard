using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RepoBoard.Core.Board;

namespace RepoBoard.Core.Sync;

/// <summary>
/// Every <see cref="Tick"/>: the local status of the repositories no file watcher sees, and of the watched ones as a
/// fallback every <see cref="BoardService.WatchedRefresh"/> (WP-03, ADR 0011), then the auto-sync of those whose interval
/// has passed (ADR 0003).
/// </summary>
public sealed class AutoSyncService(BoardService board, TimeProvider time, ILogger<AutoSyncService> logger) : BackgroundService
{
    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Tick, time);
        do
        {
            try
            {
                await board.RefreshStaleAsync(stoppingToken);
                await board.AutoSyncDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Auto-sync tick failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
