using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using RepoBoard.Core.Board;
using RepoBoard.Core.Sync;
using RepoBoard.UI.Resources;
using RepoBoard.UI.State;
using Forms = System.Windows.Forms;

namespace RepoBoard.App.Services;

/// <summary>
/// The icon in the notification area (ADR 0006): WinForms' <see cref="Forms.NotifyIcon"/> – WPF has none. Its menu opens
/// the window, fetches all, pauses auto-sync and ends RepoBoard; its balloon tips are the notifications (Windows 10/11
/// shows them as toasts, no package and no registration needed). Created and disposed on the UI thread: the icon belongs
/// to a hidden window of that thread.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const int BalloonMilliseconds = 10_000; // Windows decides; only a hint for older versions

    private readonly BoardService _board;
    private readonly Action _showWindow;
    private readonly ILogger _logger;
    private readonly Dispatcher _dispatcher;
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _pause;
    private int _tooltipQueued;
    private Action? _balloonClick;

    public TrayIcon(BoardService board, Action showWindow, Action exit, ILogger logger)
    {
        _board = board;
        _showWindow = showWindow;
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;

        var open = new Forms.ToolStripMenuItem(BoardText.Tray_Open, null, (_, _) => showWindow());
        open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold); // the double-click action
        _pause = new Forms.ToolStripMenuItem(BoardText.Tray_PauseAutoSync, null, (_, _) => board.SetAutoSyncPaused(!board.AutoSyncPaused));
        var menu = new Forms.ContextMenuStrip();
        menu.Items.AddRange([
            open,
            new Forms.ToolStripMenuItem(BoardText.FetchAll, null, (_, _) => FetchAll()),
            _pause,
            new Forms.ToolStripSeparator(),
            new Forms.ToolStripMenuItem(BoardText.Tray_Exit, null, (_, _) => exit()),
        ]);
        menu.Opening += (_, _) => _pause.Checked = board.AutoSyncPaused;

        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Text = TrayStatus.Text(board.Repos, board.AutoSyncPaused),
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                showWindow();
            }
        };
        _icon.BalloonTipClicked += (_, _) => OpenFromBalloon();
        _icon.Visible = true;
        board.Changed += OnBoardChanged;
    }

    /// <summary>A notification; a click on it opens the window and then runs <paramref name="onClick"/> (a repository's row, the update dialog).</summary>
    public void Notify(string title, string text, ActivityKind worst, Action? onClick)
    {
        _balloonClick = onClick;
        var icon = worst switch
        {
            ActivityKind.Error => Forms.ToolTipIcon.Error,
            ActivityKind.Skipped => Forms.ToolTipIcon.Warning,
            _ => Forms.ToolTipIcon.Info,
        };
        _icon.ShowBalloonTip(BalloonMilliseconds, title, text, icon);
    }

    /// <summary>Raised from any thread and in bursts: one tooltip update per burst, on the UI thread.</summary>
    private void OnBoardChanged()
    {
        if (Interlocked.Exchange(ref _tooltipQueued, 1) == 0)
        {
            _dispatcher.BeginInvoke(() =>
            {
                Volatile.Write(ref _tooltipQueued, 0);
                _icon.Text = TrayStatus.Text(_board.Repos, _board.AutoSyncPaused);
            }, DispatcherPriority.Background);
        }
    }

    private void OpenFromBalloon()
    {
        _showWindow();
        _balloonClick?.Invoke();
    }

    private async void FetchAll()
    {
        try
        {
            await _board.FetchAllAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fetch all from the notification area failed");
        }
    }

    private static System.Drawing.Icon LoadIcon()
    {
        using var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/RepoBoard.ico")).Stream;
        return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _board.Changed -= OnBoardChanged;
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Icon?.Dispose();
        _icon.Dispose();
    }
}
