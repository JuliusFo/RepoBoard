using RepoBoard.Core.Git;
using RepoBoard.Core.Repos;

namespace RepoBoard.Core.Board;

/// <summary>
/// What the board knows about one repository. Written by <see cref="BoardService"/> (also from background threads), read
/// by the UI: every property is replaced as a whole, never changed in place.
/// </summary>
public sealed class RepoState(RepoEntry entry)
{
    public RepoEntry Entry { get; internal set; } = entry;
    public string Path => Entry.Path;
    public string Name { get; } = System.IO.Path.GetFileName(entry.Path.TrimEnd('\\', '/'));
    public string? GitDir { get; internal set; }
    public RepoStatus? Status { get; internal set; }
    public RepoDetails? Details { get; internal set; }

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

    /// <summary>From FETCH_HEAD, so a fetch from the IDE or terminal counts too.</summary>
    public DateTimeOffset? LastFetch { get; internal set; }

    public DateTimeOffset? LastAutoSync { get; internal set; }

    public bool AutoSyncOn => Entry.AutoFetch || Entry.AutoPull;

    /// <summary>One git operation per repository at a time.</summary>
    internal SemaphoreSlim Gate { get; } = new(1, 1);
}
