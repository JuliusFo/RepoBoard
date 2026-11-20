using RepoBoard.Core.Board;
using RepoBoard.Core.Repos;
using RepoBoard.Core.Resources;

namespace RepoBoard.Core.Sync;

/// <summary>A Windows notification about what auto-sync did (ADR 0006).</summary>
/// <param name="Worst">The most serious entry in it: <see cref="ActivityKind.Error"/>, <see cref="ActivityKind.Skipped"/> or <see cref="ActivityKind.Pulled"/>.</param>
/// <param name="RepoPath">The repository a click opens; null if the notification is about several.</param>
public sealed record SyncNotification(string Title, string Text, ActivityKind Worst, string? RepoPath);

/// <summary>
/// Turns what auto-sync logs into notifications: only auto-sync entries the setting asks for (decision of the user:
/// auto-pulls and problems by default), one notification per auto-sync run – however long its fetches take – so a run
/// that pulls five repositories shows one notification, not five replacing each other. An entry outside a run (a single
/// repository synced directly) waits <see cref="Gather"/> for company. The same failure on every tick is already logged
/// once (BoardService).
/// </summary>
public sealed class SyncNotifier : IDisposable
{
    public static readonly TimeSpan Gather = TimeSpan.FromSeconds(2);

    /// <summary>What Windows shows of a notification (<c>NOTIFYICONDATA.szInfoTitle</c>/<c>szInfo</c> without the terminator).</summary>
    public const int TitleLimit = 63;

    /// <inheritdoc cref="TitleLimit"/>
    public const int TextLimit = 255;

    private const int MaxLines = 4;

    private readonly BoardService _board;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();
    private List<ActivityEntry> _pending = [];
    private ITimer? _timer;

    public SyncNotifier(BoardService board, TimeProvider time)
    {
        _board = board;
        _time = time;
        board.Activity.Added += OnAdded;
        board.AutoSyncRunEnded += Flush;
    }

    /// <summary>Raised on a pool thread; the host marshals and decides whether to show it (not over its own window).</summary>
    public event Action<SyncNotification>? Show;

    public static bool Wanted(ActivityEntry entry, NotificationLevel level) =>
        entry.IsAuto && level switch
        {
            NotificationLevel.Problems => entry.Kind is ActivityKind.Error or ActivityKind.Skipped,
            NotificationLevel.PullsAndProblems => entry.Kind is ActivityKind.Error or ActivityKind.Skipped or ActivityKind.Pulled,
            _ => false,
        };

    /// <summary>One notification for <paramref name="entries"/> (in the order they came), problems first.</summary>
    public static SyncNotification Compose(IReadOnlyList<ActivityEntry> entries)
    {
        ArgumentOutOfRangeException.ThrowIfZero(entries.Count);
        var ordered = entries.OrderBy(e => Severity(e.Kind)).ToList(); // stable: same kind keeps its order
        var repos = entries.Select(Key).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var single = repos == 1;
        var title = single ? entries[0].Repo : TextFormat.Plural(repos, SyncText.Notify_TitleOne, SyncText.Notify_TitleOther);
        var lines = ordered.Take(MaxLines).Select(e => single ? e.Text : TextFormat.Format(SyncText.Notify_Line, e.Repo, e.Text)).ToList();
        if (ordered.Count > MaxLines)
        {
            lines.Add(TextFormat.Format(SyncText.Notify_More, ordered.Count - MaxLines));
        }

        return new SyncNotification(Cut(title, TitleLimit), Cut(string.Join('\n', lines), TextLimit), ordered[0].Kind,
            single ? entries[0].RepoPath : null);
    }

    private void OnAdded(ActivityEntry entry)
    {
        if (!Wanted(entry, _board.Settings.Notifications))
        {
            return;
        }

        lock (_lock)
        {
            _pending.Add(entry);
            if (!_board.AutoSyncRunning)
            {
                _timer ??= _time.CreateTimer(_ => Flush(), null, Gather, Timeout.InfiniteTimeSpan); // the run's end sends otherwise
            }
        }
    }

    private void Flush()
    {
        List<ActivityEntry> entries;
        lock (_lock)
        {
            entries = _pending;
            _pending = [];
            _timer?.Dispose();
            _timer = null;
        }

        if (entries.Count > 0)
        {
            Show?.Invoke(Compose(entries));
        }
    }

    private static int Severity(ActivityKind kind) => kind switch
    {
        ActivityKind.Error => 0,
        ActivityKind.Skipped => 1,
        _ => 2,
    };

    private static string Key(ActivityEntry entry) => entry.RepoPath ?? entry.Repo;

    private static string Cut(string text, int limit) => text.Length <= limit ? text : string.Concat(text.AsSpan(0, limit - 1), "…");

    public void Dispose()
    {
        _board.Activity.Added -= OnAdded;
        _board.AutoSyncRunEnded -= Flush;
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}
