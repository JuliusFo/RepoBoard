using RepoBoard.Core.Git;
using RepoBoard.Core.Resources;

namespace RepoBoard.Core.Sync;

/// <summary>When auto-pull keeps out (ADR 0003): only a plain fast-forward of a branch that has only fallen behind.</summary>
public static class PullCheck
{
    /// <summary>Why the fast-forward must not run, as text for the user; null if nothing speaks against it.</summary>
    public static string? Blocker(RepoStatus status, GitOperation? running) =>
        status.IsDetached ? SyncText.Blocker_Detached
        : !status.HasUpstream ? SyncText.Blocker_NoUpstream
        : running is { } op ? TextFormat.Format(SyncText.Blocker_Operation, Name(op))
        : status.Conflicts > 0 ? SyncText.Blocker_Conflicts
        : status.Ahead > 0 ? TextFormat.Format(SyncText.Blocker_Diverged, status.Ahead, status.Behind)
        : null;

    public static string Name(GitOperation operation) => operation switch
    {
        GitOperation.Merge => SyncText.Operation_Merge,
        GitOperation.Rebase => SyncText.Operation_Rebase,
        GitOperation.CherryPick => SyncText.Operation_CherryPick,
        _ => SyncText.Operation_Revert,
    };
}
