using System.Globalization;
using RepoBoard.Core.Resources;
using RepoBoard.UI.Resources;

namespace RepoBoard.UI.State;

/// <summary>"3 min ago" for the last fetch; an old fetch is a hint that something is off.</summary>
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
            : TextFormat.Format(BoardText.Ago_Days, (int)age.TotalDays);
    }

    public static bool IsStale(DateTimeOffset? time, DateTimeOffset now) => time is null || now - time > Stale;

    /// <summary>The exact time for the tooltip, in the user's format.</summary>
    public static string Exact(DateTimeOffset? time) =>
        time?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "";
}
