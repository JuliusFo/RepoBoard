using System.Globalization;
using RepoBoard.Core.Git;
using RepoBoard.Core.Resources;
using RepoBoard.UI.Resources;

namespace RepoBoard.UI.State;

/// <summary>
/// "3 min ago" in the UI's language: the last fetch (an old one is a hint that something is off), a commit's date in the
/// history and the branch list – read from git as a number, not as git's text in git's language (review 0.4.0, A19).
/// </summary>
public static class RelativeTime
{
    /// <summary>A fetch older than this is marked (warning colour).</summary>
    public static readonly TimeSpan Stale = TimeSpan.FromDays(1);

    public static string Text(DateTimeOffset? time, DateTimeOffset now)
    {
        if (time is not { } t)
        {
            return BoardText.Ago_Never;
        }

        var age = now - t;
        return age.TotalMinutes < 1 ? BoardText.Ago_JustNow
            : age.TotalHours < 1 ? TextFormat.Format(BoardText.Ago_Minutes, (int)age.TotalMinutes)
            : age.TotalDays < 1 ? TextFormat.Format(BoardText.Ago_Hours, (int)age.TotalHours)
            : age.TotalDays < 2 ? BoardText.Ago_Yesterday
            : age.TotalDays < 60 ? TextFormat.Format(BoardText.Ago_Days, (int)age.TotalDays)
            : age.TotalDays < 730 ? TextFormat.Format(BoardText.Ago_Months, (int)(age.TotalDays / 30.4))
            : TextFormat.Format(BoardText.Ago_Years, (int)(age.TotalDays / 365.25));
    }

    /// <summary>How a branch stands to its upstream for the branch list: "1 ahead, 2 behind", "upstream deleted", or nothing.</summary>
    public static string Track(BranchInfo branch) =>
        branch.Gone ? BoardText.UpstreamGone
        : string.Join(", ", new[]
        {
            branch.Ahead > 0 ? TextFormat.Format(BoardText.Branch_Ahead, branch.Ahead) : null,
            branch.Behind > 0 ? TextFormat.Format(BoardText.Branch_Behind, branch.Behind) : null,
        }.OfType<string>());

    public static bool IsStale(DateTimeOffset? time, DateTimeOffset now) => time is null || now - time > Stale;

    /// <summary>The exact time for the tooltip, in the user's format.</summary>
    public static string Exact(DateTimeOffset? time) =>
        time?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "";
}
