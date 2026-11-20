namespace RepoBoard.Core.Repos;

/// <summary>One repository on the board and its auto-sync switches.</summary>
/// <param name="AutoPull">Fast-forward after each auto-fetch; implies <paramref name="AutoFetch"/> (ADR 0003).</param>
public sealed record RepoEntry(string Path, bool AutoFetch = false, bool AutoPull = false);

/// <summary>When auto-sync shows a Windows notification (ADR 0006).</summary>
public enum NotificationLevel
{
    Off,

    /// <summary>Failures and skipped auto-pulls.</summary>
    Problems,

    /// <summary>Every auto-pull that brought commits, and the problems.</summary>
    PullsAndProblems,
}

/// <summary>
/// How the app behaves beside the board (ADR 0006). Start with Windows is not here: the Run entry in the registry is the
/// truth, the user can switch it off in the Task Manager.
/// </summary>
public sealed record AppSettings
{
    /// <summary>Closing the window keeps RepoBoard (and auto-sync) running in the notification area.</summary>
    public bool CloseToTray { get; init; } = true;

    public NotificationLevel Notifications { get; init; } = NotificationLevel.PullsAndProblems;

    /// <summary>The hint "keeps running in the notification area" came once.</summary>
    public bool TrayHintShown { get; init; }
}

/// <summary>The content of <see cref="AppPaths.ConfigFile"/>.</summary>
public sealed record BoardConfig
{
    public const int CurrentVersion = 1;

    /// <summary>The intervals offered for auto-sync, in minutes.</summary>
    public static readonly int[] Intervals = [1, 2, 5, 10, 15, 30, 60];

    public const int DefaultAutoSyncMinutes = 5;

    public int Version { get; init; } = CurrentVersion;

    public IReadOnlyList<RepoEntry> Repos { get; init; } = [];

    /// <summary>Minutes between two auto-syncs of one repository.</summary>
    public int AutoSyncMinutes { get; init; } = DefaultAutoSyncMinutes;

    public AppSettings Settings { get; init; } = new();
}
