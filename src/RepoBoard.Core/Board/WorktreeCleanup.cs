namespace RepoBoard.Core.Board;

/// <summary>Why "clean up" suggests a worktree (WP-04, decision of the user).</summary>
public enum WorktreeReason
{
    /// <summary>Its branch is contained in the remote's default branch.</summary>
    Merged,

    /// <summary>Its branch's upstream was deleted on the remote ("gone"), as after a merged pull request – squash merges too.</summary>
    UpstreamGone,

    /// <summary>Detached on a commit the remote's default branch contains.</summary>
    DetachedMerged,

    /// <summary>Its folder is gone; only git's data about it is left.</summary>
    FolderMissing,
}

/// <summary>Why a worktree is not removed (ADR 0010): it is shown, never removed.</summary>
public enum WorktreeBlock
{
    /// <summary>Changed, staged or new files.</summary>
    Changes,

    /// <summary>Locked with <c>git worktree lock</c>.</summary>
    Locked,

    /// <summary>A merge, rebase, cherry-pick or revert is unfinished in it.</summary>
    Operation,

    /// <summary>Detached with commits no branch or tag contains: they would be lost with the worktree's reflog.</summary>
    UnreferencedCommits,

    /// <summary>git could not read its status or its ignored files.</summary>
    Unknown,
}

/// <summary>One worktree and what removing it would mean.</summary>
/// <param name="Reason">Why clean-up suggests it; null when the user picked it.</param>
/// <param name="Block">Why it stays; null when it may go.</param>
/// <param name="Ignored">The ignored files and folders git deletes with it, unasked (<c>bin/</c>, <c>.env</c>).</param>
public sealed record WorktreeCandidate(RepoState Worktree, WorktreeReason? Reason, WorktreeBlock? Block, IReadOnlyList<string> Ignored)
{
    public bool CanRemove => Block is null;

    /// <summary>
    /// Ticked when the question opens: the worktree the user picked, and those whose remote side is gone (upstream deleted,
    /// folder gone). "Merged" and "detached" cannot tell a done branch from a session's fresh one whose base moved on: listed,
    /// not ticked (review WP-04, decision of the user).
    /// </summary>
    public bool Ticked => CanRemove && Reason is null or WorktreeReason.UpstreamGone or WorktreeReason.FolderMissing;
}

/// <summary>What clean-up found in one repository.</summary>
/// <param name="DefaultBranch">The remote's default branch (<c>origin/main</c>) merged branches are measured against; null without <c>origin/HEAD</c>.</param>
public sealed record WorktreeCleanup(string? DefaultBranch, IReadOnlyList<WorktreeCandidate> Candidates);
