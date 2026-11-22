using RepoBoard.Core.Board;
using RepoBoard.Core.Git;
using RepoBoard.Core.Resources;
using RepoBoard.Core.Sync;

namespace RepoBoard.UI.State;

/// <summary>
/// Whether the commit tools of WP-09 are open for a repository, and if not why – for the buttons and their tooltips. The
/// board checks the same again inside the repository's gate right before git runs (ADR 0013); this is what the page
/// knows from the last status.
/// </summary>
public static class CommitTools
{
    /// <summary>Why the last commit cannot be amended; null if it can.</summary>
    public static string? AmendBlocker(RepoState repo) =>
        repo.Status is not { } status || repo.Details is not { } details ? SyncText.Pick_Unknown
        : status.IsInitial ? SyncText.Pick_Initial
        : repo.Operation is { } running ? TextFormat.Format(SyncText.Pick_Operation, PullCheck.Name(running))
        : details.HeadOnRemote ? SyncText.Amend_Pushed
        : null;

    /// <summary>Why <paramref name="commit"/> cannot be reverted (or cherry-picked); null if it can.</summary>
    public static string? PickBlocker(RepoState repo, CommitDetails commit, bool revert) =>
        commit.IsMerge ? SyncText.Pick_Merge
        : repo.Status is not { } status ? SyncText.Pick_Unknown
        : repo.Operation is { } running ? TextFormat.Format(SyncText.Pick_Operation, PullCheck.Name(running))
        : status.IsInitial ? SyncText.Pick_Initial
        : status.Changes.Any(c => !c.Untracked) ? SyncText.Pick_Changes
        : revert && !commit.InHead ? SyncText.Revert_NotInHead
        : !revert && commit.InHead ? SyncText.CherryPick_InHead
        : null;
}
