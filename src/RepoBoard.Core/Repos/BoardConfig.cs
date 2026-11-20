namespace RepoBoard.Core.Repos;

/// <summary>One repository on the board and its auto-sync switches.</summary>
/// <param name="AutoPull">Fast-forward after each auto-fetch; implies <paramref name="AutoFetch"/> (ADR 0003).</param>
public sealed record RepoEntry(string Path, bool AutoFetch = false, bool AutoPull = false);

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
}
