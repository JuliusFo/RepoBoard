using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RepoBoard.Core.Board;

namespace RepoBoard.Core.Sync;

/// <summary>
/// Every <see cref="Tick"/>: the local status of the repositories no file watcher sees, and of the watched ones as a
/// fallback every <see cref="BoardService.WatchedRefresh"/> (WP-03, ADR 0011), then the auto-sync of those whose interval
/// has passed (ADR 0003). The tick does not wait for the auto-sync: offline, a run of many repositories takes minutes, and
/// the status of repositories without a watcher must still be read meanwhile (second review pass R1). One run at a time.
/// </summary>
public sealed class AutoSyncService(BoardService board, TimeProvider time, ILogger<AutoSyncService> logger) : BackgroundService
{
    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Tick, time);
        Task? sync = null;
        try
        {
            do
            {
                try
                {
                    await board.RefreshStaleAsync(stoppingToken);
                    if (sync is null || sync.IsCompleted)
                    {
                        sync = SyncAsync(stoppingToken);
                    }
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
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (sync is not null)
            {
                await sync; // never throws: SyncAsync keeps its failures
            }
        }
    }

    private async Task SyncAsync(CancellationToken ct)
    {
        try
        {
            await board.AutoSyncDueAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Auto-sync run failed");
        }
    }
}
