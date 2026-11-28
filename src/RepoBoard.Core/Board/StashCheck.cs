using RepoBoard.Core.Git;
using RepoBoard.Core.Resources;
using RepoBoard.Core.Sync;

namespace RepoBoard.Core.Board;

/// <summary>
/// When discarding and the stash keep out (WP-06, ADR 0016), as text for the user; null if nothing speaks against it. One set
/// of rules for the page's buttons (from the last status) and the board's check inside the gate (from a status read right
/// before), as <see cref="PickCheck"/>.
/// </summary>
public static class StashCheck
{
    /// <summary>
    /// A change "Discard" offers: an unstaged change of a tracked file or a new file. Not a conflict (resolve it first), not
    /// a repository of its own inside the worktree – its history would go to the recycle bin with it.
    /// </summary>
    public static bool CanDiscard(FileChange file) => file.IsUnstaged && !file.Conflict && !file.IsNestedRepository;

    /// <summary>
    /// What speaks against discarding <paramref name="files"/>: tracked ones go into a stash entry – git keeps one only on top
    /// of a commit and cannot write it while a conflict is open. New files alone go to the recycle bin and need neither.
    /// </summary>
    public static string? Discard(RepoStatus status, IEnumerable<string> files)
    {
        var tracked = files.Any(path => status.Changes.Any(c => c.Path == path && !c.Untracked));
        return !tracked ? null
            : status.IsInitial ? SyncText.Pick_Initial
            : status.Conflicts > 0 ? SyncText.Stash_Conflicts
            : null;
    }

    /// <summary>What speaks against stashing the changes (<paramref name="includeUntracked"/>: new files too).</summary>
    public static string? Create(RepoStatus status, GitOperation? running, bool includeUntracked) =>
        status.IsInitial ? SyncText.Pick_Initial
        : running is { } op ? TextFormat.Format(SyncText.Pick_Operation, PullCheck.Name(op))
        : status.Conflicts > 0 ? SyncText.Stash_Conflicts
        : status.Changes.Any(c => !c.Untracked) ? null
        : includeUntracked && status.Changes.Any(c => c.Untracked) ? null
        : status.Changes.Any(c => c.Untracked) ? SyncText.Stash_OnlyNew
        : SyncText.Stash_Nothing;

    /// <summary>What speaks against applying (or popping) a stash entry; git itself refuses to overwrite a local change.</summary>
    public static string? Apply(RepoStatus status, GitOperation? running) =>
        running is { } op ? TextFormat.Format(SyncText.Pick_Operation, PullCheck.Name(op))
        : status.Conflicts > 0 ? SyncText.Stash_Conflicts
        : null;
}
