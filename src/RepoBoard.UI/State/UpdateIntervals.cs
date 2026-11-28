using RepoBoard.Core.Resources;
using RepoBoard.UI.Resources;

namespace RepoBoard.UI.State;

/// <summary>
/// The labels of the update check's intervals (<see cref="Core.Repos.AppSettings.UpdateCheckIntervals"/>). An interval the
/// list gets later has a label of its own: every value not named fell to "every 4 hours" (review 0.4.0, B8).
/// </summary>
public static class UpdateIntervals
{
    public static string Label(int hours) => hours switch
    {
        0 => UpdateText.Interval_Never,
        1 => UpdateText.Interval_Hourly,
        4 => UpdateText.Interval_Every4Hours,
        24 => UpdateText.Interval_Daily,
        _ => TextFormat.Format(UpdateText.Interval_EveryHours, hours),
    };
}
