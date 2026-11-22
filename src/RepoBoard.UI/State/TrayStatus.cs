using RepoBoard.Core.Board;
using RepoBoard.Core.Resources;
using RepoBoard.UI.Resources;

namespace RepoBoard.UI.State;

/// <summary>
/// The tooltip of the notification-area icon: how many repositories, how many behind or with problems, a pause. It counts
/// repositories only, not their worktrees like the chips do: it says "repositories" (review WP-04, decision of the user).
/// </summary>
public static class TrayStatus
{
    /// <summary>What Windows shows of the tooltip (<c>NOTIFYICONDATA.szTip</c> without the terminator).</summary>
    public const int Limit = 127;

    public static string Text(IReadOnlyList<RepoState> repos, bool autoSyncPaused)
    {
        var parts = new List<string> { TextFormat.Plural(repos.Count, BoardText.Tray_ReposOne, BoardText.Tray_ReposOther) };
        var behind = repos.Count(r => BoardFilter.Matches(BoardFilterKind.Behind, r));
        if (behind > 0)
        {
            parts.Add(TextFormat.Format(BoardText.Tray_Behind, behind));
        }

        var problems = repos.Count(r => BoardFilter.Matches(BoardFilterKind.Problems, r));
        if (problems > 0)
        {
            parts.Add(TextFormat.Format(BoardText.Tray_Problems, problems));
        }

        var text = $"RepoBoard\n{string.Join(" · ", parts)}";
        if (autoSyncPaused)
        {
            text += $"\n{BoardText.Paused}";
        }

        return TextCut.Cut(text, Limit);
    }
}
