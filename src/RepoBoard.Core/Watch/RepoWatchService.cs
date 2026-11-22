using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RepoBoard.Core.Board;

namespace RepoBoard.Core.Watch;

/// <summary>
/// Reads a repository's status as soon as something changes in it – a commit, a branch switch or a saved file in the IDE
/// or terminal (WP-03, ADR 0011). One recursive <see cref="FileSystemWatcher"/> per folder of <see cref="WatchMap.Roots"/>;
/// a series of changes is read once (<see cref="Quiet"/>, at the latest after <see cref="Longest"/>), through
/// <see cref="BoardService.RefreshAsync"/> and its gate. What the watchers see, the status tick reads only as a fallback
/// (<see cref="BoardService.WatchedRefresh"/>); a watcher that fails leaves its repositories to the tick and is tried
/// again after <see cref="RetryAfter"/>. Network folders and drive roots are left to the tick from the start.
/// </summary>
public sealed class RepoWatchService(BoardService board, TimeProvider time, ILogger<RepoWatchService> logger) : BackgroundService
{
    public static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(750);
    public static readonly TimeSpan Longest = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    /// <summary>After an unexpected failure of the loop: a pause, so a failure that repeats does not spin.</summary>
    private static readonly TimeSpan PauseAfterFailure = TimeSpan.FromSeconds(5);

    /// <summary>As many status reads at once as <see cref="BoardService.RefreshAllAsync"/> runs.</summary>
    private const int MaxParallelReads = 8;

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly Lock _lock = new();
    private readonly ChangeDebouncer<RepoState> _pending = new(Ticks(Quiet, time), Ticks(Longest, time));
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(PathComparer);
    private readonly Dictionary<string, long> _failed = new(PathComparer);
    private readonly Dictionary<string, string> _warned = new(PathComparer);
    private readonly HashSet<string> _broken = new(PathComparer);

    // Never disposed: without AvailableWaitHandle there is nothing to free, and a watcher's callback or a read still
    // running when the host is disposed would otherwise throw (review WP-03).
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly SemaphoreSlim _reads = new(MaxParallelReads);
    private volatile WatchMap _map = WatchMap.Empty;
    private int _boardChanged = 1;
    private int _rebuild = 1;

    /// <summary>The folders watched right now.</summary>
    internal IReadOnlyList<string> WatchedRoots
    {
        get
        {
            lock (_lock)
            {
                return [.. _watchers.Keys];
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        board.Changed += OnBoardChanged;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (Interlocked.Exchange(ref _boardChanged, 0) == 1 || Volatile.Read(ref _rebuild) == 1)
                    {
                        Reconcile();
                    }

                    ReadDue(stoppingToken);
                    await WaitAsync(NextWake(), stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Like the tick: one surprise must not end instant refresh for good – or stop the host (review WP-03).
                    logger.LogError(ex, "File watching failed, trying again");
                    Interlocked.Exchange(ref _rebuild, 1);
                    await Task.Delay(PauseAfterFailure, time, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            board.Changed -= OnBoardChanged;
            lock (_lock)
            {
                foreach (var watcher in _watchers.Values)
                {
                    watcher.Dispose();
                }

                _watchers.Clear();
            }

            foreach (var state in _map.States)
            {
                state.Watched = false;
            }
        }
    }

    private void OnBoardChanged()
    {
        Interlocked.Exchange(ref _boardChanged, 1);
        Wake();
    }

    /// <summary>
    /// Brings the watchers in line with the board: new repositories and worktrees, removed ones, a folder that went or came
    /// back, a watcher that failed. Only when something of that changed (<see cref="WatchMap.Matches"/>) – the board
    /// changes with every status.
    /// </summary>
    private void Reconcile()
    {
        var repos = board.Repos;
        var rebuild = Interlocked.Exchange(ref _rebuild, 0) == 1;
        if (!rebuild && _map.Matches(repos))
        {
            return;
        }

        var map = _map = WatchMap.Of(repos);
        var now = time.GetTimestamp();
        lock (_lock)
        {
            foreach (var root in _broken)
            {
                Stop(root);
                _failed[root] = now;
            }

            _broken.Clear();
            foreach (var (root, failedAt) in _failed.ToList())
            {
                if (RetryDue(failedAt, now) || !map.Roots.Contains(root, PathComparer))
                {
                    _failed.Remove(root);
                }
            }

            // A folder warned about stays quiet while it fails the same way – also a watcher that starts and fails after
            // (review WP-03); off the board, it is forgotten.
            lock (_warned)
            {
                foreach (var root in _warned.Keys.Where(r => !map.Roots.Contains(r, PathComparer)).ToList())
                {
                    _warned.Remove(root);
                }
            }

            var wanted = map.Roots.Where(r => Directory.Exists(r) && !LeftToTheTick(r)).ToHashSet(PathComparer);
            foreach (var root in _watchers.Keys.Where(r => !wanted.Contains(r)).ToList())
            {
                Stop(root);
            }

            foreach (var root in wanted.Where(r => !_watchers.ContainsKey(r) && !_failed.ContainsKey(r)))
            {
                Start(root, now);
            }

            var watched = _watchers.Keys.ToList();
            foreach (var state in map.States)
            {
                state.Watched = map.IsCovered(state, watched);

                // A worktree just added (its repository read the list) has no status yet: read it now, not with the tick.
                // As a change at the dawn of time it is due at once, and any read since – the tick at the start – will do.
                if (state.StatusReadAt is null)
                {
                    _pending.Add(state, long.MinValue);
                }
            }
        }
    }

    private bool RetryDue(long failedAt, long now) => time.GetElapsedTime(failedAt, now) >= RetryAfter;

    /// <summary>
    /// A network folder may take a watcher and still report nothing (SMB, <c>\\wsl$</c>): it stays with the 20 s tick, as the
    /// user decided for folders without a working watcher. A drive root – a worktree entry pointing there – would watch
    /// the whole drive (review WP-03).
    /// </summary>
    private static bool LeftToTheTick(string root)
    {
        if (Path.GetPathRoot(root) is not { } driveRoot || WatchMap.Normalize(driveRoot) is not { } normalizedRoot
            || string.Equals(normalizedRoot, root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            return new DriveInfo(driveRoot).DriveType == DriveType.Network;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void Start(string root, long now)
    {
        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                InternalBufferSize = 64 * 1024, // the most Windows takes; a build overflows a smaller one
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            };
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnRenamed;
            watcher.Error += (_, e) => OnError(root, e.GetException());
            watcher.EnableRaisingEvents = true;
            _watchers[root] = watcher;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // The inotify limit on Linux, a folder gone meanwhile: the tick reads it every 20 s.
            watcher?.Dispose();
            _failed[root] = now;
            Warn(root, ex.Message, null);
        }
    }

    private void Stop(string root)
    {
        if (_watchers.Remove(root, out var watcher))
        {
            watcher.Dispose();
        }
    }

    /// <summary>The same failure of a folder once a warning, then on every retry only for debugging (review WP-03).</summary>
    private void Warn(string root, string message, Exception? ex)
    {
        bool first;
        lock (_warned)
        {
            first = !(_warned.TryGetValue(root, out var last) && last == message);
            _warned[root] = message;
        }

        logger.Log(first ? LogLevel.Warning : LogLevel.Debug, ex, "Cannot watch {Folder}, its status is read on each tick: {Message}", root, message);
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => Changed(e.FullPath);

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        Changed(e.OldFullPath);
        Changed(e.FullPath);
    }

    private void Changed(string path)
    {
        var stale = _map.Route(path);
        if (stale.Count == 0)
        {
            return;
        }

        var now = time.GetTimestamp();
        lock (_lock)
        {
            foreach (var state in stale)
            {
                _pending.Add(state, now);
            }
        }

        Wake();
    }

    /// <summary>
    /// A full buffer lost events: everything under the watcher is read once. Any other error (the folder went, the network
    /// drive dropped) stops the watcher; its repositories fall back to the tick until it is tried again.
    /// </summary>
    internal void OnError(string root, Exception ex)
    {
        var now = time.GetTimestamp();
        var overflow = ex is InternalBufferOverflowException;
        lock (_lock)
        {
            if (overflow)
            {
                foreach (var state in _map.Under(root))
                {
                    _pending.Add(state, now);
                }
            }
            else
            {
                _broken.Add(root);
                Interlocked.Exchange(ref _rebuild, 1);
            }
        }

        if (overflow)
        {
            logger.LogDebug("Watcher buffer of {Folder} overflowed, reading its repositories again", root);
        }
        else
        {
            Warn(root, ex.Message, ex);
        }

        Wake();
    }

    private void ReadDue(CancellationToken ct)
    {
        IReadOnlyList<(RepoState State, long Last)> due;
        lock (_lock)
        {
            due = _pending.TakeDue(time.GetTimestamp());
        }

        foreach (var (state, last) in due)
        {
            _ = ReadAsync(state, last, ct);
        }
    }

    /// <summary>
    /// Reads the status unless a read began after the last change (a click, the tick, the end of an operation). A
    /// repository busy with an operation skips the read; it is tried again after <see cref="Quiet"/>. A read that fails is
    /// not tried again here – the tick reads a repository with an error every time.
    /// </summary>
    private async Task ReadAsync(RepoState state, long last, CancellationToken ct)
    {
        try
        {
            var skipped = false;
            await _reads.WaitAsync(ct);
            try
            {
                if (!(state.StatusReadAt >= last))
                {
                    skipped = !await board.RefreshAsync(state, ct);
                }
            }
            finally
            {
                _reads.Release();
            }

            if (skipped)
            {
                lock (_lock)
                {
                    _pending.Retry(state, last, time.GetTimestamp() + Ticks(Quiet, time));
                }

                Wake();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Reading the status of {Repo} after a change failed", state.Path);
        }
    }

    /// <summary>Until the next series is due or a failed watcher may be tried again; null: until something happens.</summary>
    private TimeSpan? NextWake()
    {
        long? next;
        lock (_lock)
        {
            next = _pending.NextDue;
            foreach (var failed in _failed.Values)
            {
                var retry = failed + Ticks(RetryAfter, time);
                next = next is null || retry < next ? retry : next;
            }
        }

        if (next is null)
        {
            return null;
        }

        var now = time.GetTimestamp();
        lock (_lock)
        {
            if (_failed.Values.Any(f => RetryDue(f, now)))
            {
                Interlocked.Exchange(ref _rebuild, 1);
            }
        }

        return next <= now ? TimeSpan.Zero : time.GetElapsedTime(now, next.Value);
    }

    private async Task WaitAsync(TimeSpan? delay, CancellationToken ct)
    {
        if (delay == TimeSpan.Zero)
        {
            return;
        }

        if (delay is not { } wait)
        {
            await _wake.WaitAsync(ct);
            return;
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await Task.WhenAny(_wake.WaitAsync(stop.Token), Task.Delay(wait, time, stop.Token));
        await stop.CancelAsync();
        ct.ThrowIfCancellationRequested();
    }

    private void Wake()
    {
        try
        {
            if (_wake.CurrentCount == 0)
            {
                _wake.Release();
            }
        }
        catch (SemaphoreFullException)
        {
            // woken already
        }
    }

    private static long Ticks(TimeSpan span, TimeProvider time) => (long)(span.TotalSeconds * time.TimestampFrequency);
}
