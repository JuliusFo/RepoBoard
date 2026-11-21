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
    public ImmutableList<RepoState> Worktrees { get; internal set; } = [];

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

    /// <summary>Worktrees have no auto-sync of their own: they share the refs a fetch of their repository brings.</summary>
    public bool AutoSyncOn => !IsWorktree && (Entry.AutoFetch || Entry.AutoPull);

    /// <summary>One git operation per repository (or worktree) at a time.</summary>
    internal SemaphoreSlim Gate { get; } = new(1, 1);
}
