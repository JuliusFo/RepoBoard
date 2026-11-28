using RepoBoard.Core.Board;
using RepoBoard.Core.Git;
using RepoBoard.Core.Resources;
using RepoBoard.UI.Resources;

namespace RepoBoard.UI.State;

/// <summary>
/// What discarding and the stash of the opened row show (WP-06), and whether their buttons are open – if not, why, for the
/// tooltip. The rules are <see cref="StashCheck"/>'s, which the board checks again inside the gate (ADR 0016); this is what
/// the page knows from the last status.
/// </summary>
public static class StashTools
{
    /// <summary>Why discarding <paramref name="files"/> keeps out; null if it can go.</summary>
    public static string? DiscardBlocker(RepoState repo, IEnumerable<FileChange> files) =>
        repo.Status is not { } status ? SyncText.Pick_Unknown : StashCheck.Discard(status, files.Select(f => f.Path));

    /// <summary>The question's text: where the changes go – the stash list, the recycle bin or both.</summary>
    public static string DiscardText(IReadOnlyCollection<FileChange> files) =>
        files.All(f => f.Untracked) ? BoardText.Discard_TextRecycle
        : files.Any(f => f.Untracked) ? BoardText.Discard_TextBoth
        : BoardText.Discard_TextStash;

    /// <summary>Why "Stash" keeps out; null if it can go.</summary>
    public static string? CreateBlocker(RepoState repo, bool includeUntracked) =>
        repo.Status is not { } status ? SyncText.Pick_Unknown : StashCheck.Create(status, repo.Operation, includeUntracked);

    /// <summary>Why "Apply" and "Pop" keep out; null if they can go.</summary>
    public static string? ApplyBlocker(RepoState repo) =>
        repo.Status is not { } status ? SyncText.Pick_Unknown : StashCheck.Apply(status, repo.Operation);
}
