using RepoBoard.Core.Git;
using RepoBoard.Core.Resources;
using RepoBoard.Core.Sync;

namespace RepoBoard.Core.Board;

/// <summary>
/// When revert, cherry-pick and amend keep out (WP-09, ADR 0013), as text for the user; null if nothing speaks against it.
/// One set of rules for the page's buttons (<c>CommitTools</c>, from the last status) and the board's check inside the gate
/// (from a status read right before) – two copies had drifted apart (review 0.4.0, B2).
/// </summary>
public static class PickCheck
{
    /// <summary>What the status alone says against a revert or cherry-pick of a commit.</summary>
    public static string? Before(bool isMerge, RepoStatus status, GitOperation? running) =>
        isMerge ? SyncText.Pick_Merge
        : running is { } op ? TextFormat.Format(SyncText.Pick_Operation, PullCheck.Name(op))
        : status.IsInitial ? SyncText.Pick_Initial
        : status.Changes.Any(c => !c.Untracked) ? SyncText.Pick_Changes
        : null;

    /// <summary>The side of HEAD the commit is on: a revert only of one HEAD has, a cherry-pick only of one it lacks, nor its change.</summary>
    public static string? Side(bool revert, bool inHead, bool applied) =>
        revert && !inHead ? SyncText.Revert_NotInHead
        : !revert && inHead ? SyncText.CherryPick_InHead
        : !revert && applied ? SyncText.CherryPick_Applied
        : null;

    /// <summary>Whether the last commit may be amended: not while an operation runs, not once on a remote (or when git cannot tell).</summary>
    public static string? Amend(RepoStatus status, GitOperation? running, bool? headOnRemote) =>
        status.IsInitial ? SyncText.Pick_Initial
        : running is { } op ? TextFormat.Format(SyncText.Pick_Operation, PullCheck.Name(op))
        : OnRemote(headOnRemote);

    /// <summary>Amend needs a commit no remote branch has; unknown keeps it out too, with its own reason (review 0.4.0, A38).</summary>
    public static string? OnRemote(bool? headOnRemote) =>
        headOnRemote is null ? SyncText.Amend_Unknown : headOnRemote == true ? SyncText.Amend_Pushed : null;
}
