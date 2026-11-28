using RepoBoard.Core.Board;
using RepoBoard.Core.Git;
using RepoBoard.Core.Resources;
using RepoBoard.Core.Sync;
using RepoBoard.UI.Resources;

namespace RepoBoard.UI.State;

/// <summary>
/// What the bar of an unfinished merge, rebase, cherry-pick or revert shows (WP-07), and whether its buttons and the merge
/// and rebase buttons of the branch list are open – if not, why, for the tooltip. The board checks the same again inside
/// the repository's gate right before git runs (ADR 0014); this is what the page knows from the last status.
/// </summary>
public static class OperationTools
{
    /// <summary>One line for the bar: what runs, where it stands, which commit.</summary>
    public static string Title(OperationInfo info)
    {
        var name = PullCheck.Name(info.Kind);
        return info.Kind switch
        {
            GitOperation.Merge when info.Subject is { } subject => TextFormat.Format(BoardText.Operation_MergeTitle, subject),
            GitOperation.Rebase when info is { Step: { } step, Steps: { } steps } =>
                TextFormat.Format(BoardText.Operation_RebaseTitle, info.Branch ?? "HEAD", step, steps, info.Subject ?? Short(info.Commit)),
            GitOperation.CherryPick or GitOperation.Revert when info.Commit is { } commit =>
                TextFormat.Format(BoardText.Operation_PickTitle, name, Short(commit), info.Subject ?? ""),
            _ => TextFormat.Format(BoardText.OperationRunning, name),
        };
    }

    /// <summary>The commit a skip leaves out, for the question: its subject, else its short id.</summary>
    public static string SkippedCommit(OperationInfo info) => info.Subject ?? Short(info.Commit);

    /// <summary>A merge has no commit to skip.</summary>
    public static bool CanSkip(OperationInfo info) => info.Kind != GitOperation.Merge;

    /// <summary>Why "Continue" is closed; null if it is open.</summary>
    public static string? ContinueBlocker(RepoState repo) =>
        repo.Status is not { } status ? SyncText.Pick_Unknown
        : repo.Unfinished is not { } info ? SyncText.Operation_Gone
        : status.Conflicts > 0 ? TextFormat.Format(SyncText.Continue_Conflicts, status.Conflicts)
        : info.Kind == GitOperation.Rebase && status.Changes.Any(c => !c.Untracked && c.WorkTree != '.') ? SyncText.Continue_Unstaged
        : null;

    /// <summary>
    /// Why the current branch cannot be merged into or rebased right now; null if it can. Whether a rebase would rewrite pushed
    /// commits only the board can tell (it asks git).
    /// </summary>
    public static string? MergeBlocker(RepoState repo) =>
        repo.Status is not { } status ? SyncText.Pick_Unknown
        : repo.Operation is { } running ? TextFormat.Format(SyncText.Pick_Operation, PullCheck.Name(running))
        : status.IsInitial ? SyncText.Pick_Initial
        : status.IsDetached ? SyncText.Blocker_Detached
        : status.Changes.Any(c => !c.Untracked) ? SyncText.Pick_Changes
        : null;

    /// <summary>
    /// What "Stage all" stages: everything (empty) – but with conflicts only the other files, a conflict is marked resolved on
    /// its own; null if nothing else is left.
    /// </summary>
    public static IReadOnlyList<string>? StageAllPaths(RepoState repo) =>
        repo.Status is not { Conflicts: > 0 } status ? []
        : status.Changes.Where(c => c.IsUnstaged && !c.Conflict).Select(c => c.Path).ToList() is { Count: > 0 } others ? others
        : null;

    /// <summary>
    /// What "Unstage all" unstages: everything (empty); nothing while an operation is unfinished (null) – it would put HEAD's
    /// version of every file into the index, and "Continue" would then commit a merge without the other side, so the branch
    /// counts as merged while its changes are missing (review WP-07). With conflicts but no operation (a stash that did not
    /// apply cleanly) only the staged files.
    /// </summary>
    public static IReadOnlyList<string>? UnstageAllPaths(RepoState repo) =>
        repo.Operation is not null ? null
        : repo.Status is not { Conflicts: > 0 } status ? []
        : status.Changes.Where(c => c.IsStaged).Select(c => c.Path).ToList() is { Count: > 0 } staged ? staged
        : null;

    /// <summary>The text of the abort question: a rebase resets every tracked change, the others keep what they can.</summary>
    public static string AbortText(OperationInfo info) =>
        info.Kind == GitOperation.Rebase
            ? BoardText.Operation_AbortTextRebase
            : TextFormat.Format(BoardText.Operation_AbortText, PullCheck.Name(info.Kind));

    /// <summary>What kind of conflict a file has, from the two letters of <c>git status</c> (as git names it).</summary>
    public static string ConflictKind(FileChange file) => (file.Index, file.WorkTree) switch
    {
        ('A', 'A') => BoardText.Conflict_BothAdded,
        ('D', 'D') => BoardText.Conflict_BothDeleted,
        ('D', _) => BoardText.Conflict_DeletedByUs,
        (_, 'D') => BoardText.Conflict_DeletedByThem,
        ('A', _) => BoardText.Conflict_AddedByUs,
        (_, 'A') => BoardText.Conflict_AddedByThem,
        _ => BoardText.Conflict_BothModified,
    };

    private static string Short(string? commit) => commit is null ? "" : commit[..Math.Min(7, commit.Length)];
}
