using System.Collections.Immutable;
using RepoBoard.Core.Git;
using RepoBoard.Core.Repos;

namespace RepoBoard.Core.Board;

/// <summary>
/// What the board knows about one repository – or one linked worktree of it (WP-04), which has the same state and
/// operations but is not in the configuration. Written by <see cref="BoardService"/> (also from background threads), read
/// by the UI: every property is replaced as a whole, never changed in place.
/// </summary>
public sealed class RepoState(RepoEntry entry, RepoState? mainRepo = null)
{
    public RepoEntry Entry { get; internal set; } = entry;
    public string Path => Entry.Path;
    public string Name { get; } = System.IO.Path.GetFileName(entry.Path.TrimEnd('\\', '/'));
    public string? GitDir { get; internal set; }
    public RepoStatus? Status { get; internal set; }
    public RepoDetails? Details { get; internal set; }

    /// <summary>The repository on the board this is a linked worktree of; null for a repository on the board.</summary>
    public RepoState? MainRepo { get; } = mainRepo;

    public bool IsWorktree => MainRepo is not null;

    /// <summary>For a worktree: what <c>git worktree list</c> says about it (locked, prunable).</summary>
    public WorktreeInfo? Worktree { get; internal set; }

    /// <summary>For a repository on the board: its linked worktrees, in git's order; never the main worktree itself.</summary>
    public ImmutableList<RepoState> Worktrees
    {
        get => Volatile.Read(ref _worktrees);
        internal set => Volatile.Write(ref _worktrees, value);
    }

    private ImmutableList<RepoState> _worktrees = [];

    /// <summary>
    /// The list read from git replaces <paramref name="expected"/> only if nothing changed it meanwhile: a removal while git
    /// listed would otherwise come back for a tick (review WP-04).
    /// </summary>
    internal void ReplaceWorktrees(ImmutableList<RepoState> expected, ImmutableList<RepoState> next) =>
        Interlocked.CompareExchange(ref _worktrees, next, expected);

    internal void RemoveWorktree(RepoState worktree) => ImmutableInterlocked.Update(ref _worktrees, list => list.Remove(worktree));

    /// <summary>A worktree a Claude Code session made (<c>&lt;repo&gt;\.claude\worktrees\…</c>): it may still be in use.</summary>
    public bool IsClaudeWorktree => MainRepo is { } main
        && Path.StartsWith(System.IO.Path.Combine(main.Path, ".claude", "worktrees") + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>The opened row wants branches and history; they are read with each refresh only then.</summary>
    public bool WantsDetails { get; internal set; }

    /// <summary>What runs right now (the operation's name), null when idle.</summary>
    public string? Busy { get; internal set; }

    /// <summary>The last failure, until the next success.</summary>
    public string? Error { get; internal set; }

    /// <summary>A merge, rebase, cherry-pick or revert left unfinished: auto-pull keeps out.</summary>
    public GitOperation? Operation { get; internal set; }

    /// <summary>Why auto-pull did not pull what the last auto-fetch brought (diverged branch, merge running …).</summary>
    public string? AutoPullBlocked { get; internal set; }

    public bool Missing { get; internal set; }

    /// <summary>
    /// From FETCH_HEAD, so a fetch from the IDE or terminal counts too. A worktree has its own FETCH_HEAD but shares the refs:
    /// it shows the later of its own and its repository's.
    /// </summary>
    public DateTimeOffset? LastFetch
    {
        get => MainRepo?.LastFetch is { } main && (_lastFetch is null || main > _lastFetch) ? main : _lastFetch;
        internal set => _lastFetch = value;
    }

    private DateTimeOffset? _lastFetch;

    public DateTimeOffset? LastAutoSync { get; internal set; }

    /// <summary>
    /// When the board last began a status read that succeeded (WP-03), as a <see cref="TimeProvider.GetTimestamp"/>: a
    /// change seen before that moment is in it, a file watcher's event need not read it again. Monotonic, so a clock set
    /// back does not stall the watcher; one <c>long</c>, so a read on another thread never sees half of it (review WP-03).
    /// </summary>
    internal long? StatusReadAt
    {
        get => Volatile.Read(ref _statusReadAt) is var read and not 0 ? read : null;
        set => Volatile.Write(ref _statusReadAt, value ?? 0);
    }

    private long _statusReadAt;

    /// <summary>
    /// A file watcher sees its folder and git folder (WP-03, ADR 0011): the status tick reads it only as a fallback, every
    /// <see cref="BoardService.WatchedRefresh"/>.
    /// </summary>
    internal bool Watched
    {
        get => Volatile.Read(ref _watched);
        set => Volatile.Write(ref _watched, value);
    }

    private bool _watched;

    /// <summary>Worktrees have no auto-sync of their own: they share the refs a fetch of their repository brings.</summary>
    public bool AutoSyncOn => !IsWorktree && (Entry.AutoFetch || Entry.AutoPull);

    /// <summary>One git operation per repository (or worktree) at a time.</summary>
    internal SemaphoreSlim Gate { get; } = new(1, 1);
}
