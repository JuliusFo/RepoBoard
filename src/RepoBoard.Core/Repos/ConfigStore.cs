using RepoBoard.Core.IO;
using RepoBoard.Core.Resources;

namespace RepoBoard.Core.Repos;

/// <summary>What loading the config found, and whether it may be written.</summary>
/// <param name="Problem">What to tell the user, null if all is well.</param>
/// <param name="CanSave">False for a file of a newer RepoBoard or one that could not be read: saving would destroy it.</param>
public sealed record ConfigLoad(BoardConfig Config, string? Problem, bool CanSave);

/// <summary>
/// Reads and writes <see cref="AppPaths.ConfigFile"/> (FerretSharp's rules for own JSON files: write atomically, read
/// versioned, never overwrite a file this version cannot read).
/// </summary>
public sealed class ConfigStore(string file, TimeProvider time)
{
    public string File => file;

    public async Task<ConfigLoad> LoadAsync(CancellationToken ct)
    {
        var read = await VersionedJsonFile.ReadAsync<BoardConfig>(file, BoardConfig.CurrentVersion, JsonFiles.Options, ct, normalize: Normalize);
        switch (read.State)
        {
            case JsonFileState.Ok:
                return new(read.Document!, null, true);
            case JsonFileState.Missing:
            case JsonFileState.Empty:
                return new(new BoardConfig(), null, true);
            case JsonFileState.Newer:
                return new(new BoardConfig(), TextFormat.Format(CoreText.Config_Newer, file), false);
            case JsonFileState.Broken:
                try
                {
                    var copy = VersionedJsonFile.KeepCopy(file, time.GetLocalNow());
                    return new(new BoardConfig(), TextFormat.Format(CoreText.Config_Broken, file, read.Error?.Message, copy), true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return new(new BoardConfig(), TextFormat.Format(CoreText.Config_Unreadable, file, ex.Message), false);
                }

            default:
                return new(new BoardConfig(), TextFormat.Format(CoreText.Config_Unreadable, file, read.Error?.Message), false);
        }
    }

    public Task SaveAsync(BoardConfig config, CancellationToken ct) =>
        AtomicJsonFile.WriteAsync(file, config with { Version = BoardConfig.CurrentVersion }, JsonFiles.Options, ct);

    /// <summary>A file edited by hand: no null entries, no empty paths, each repository once, a sane interval and settings.</summary>
    private static BoardConfig Normalize(BoardConfig config) => config with
    {
        Repos = [.. (config.Repos ?? []).OfType<RepoEntry>()
            .Where(r => !string.IsNullOrWhiteSpace(r.Path) && Path.IsPathFullyQualified(r.Path))
            .DistinctBy(r => Path.GetFullPath(r.Path), StringComparer.OrdinalIgnoreCase)],
        AutoSyncMinutes = BoardConfig.Intervals.Contains(config.AutoSyncMinutes) ? config.AutoSyncMinutes : BoardConfig.DefaultAutoSyncMinutes,
        Settings = config.Settings is { } settings
            ? settings with
            {
                Notifications = Enum.IsDefined(settings.Notifications) ? settings.Notifications : new AppSettings().Notifications,
                UpdateCheckHours = AppSettings.UpdateCheckIntervals.Contains(settings.UpdateCheckHours) ? settings.UpdateCheckHours : AppSettings.DefaultUpdateCheckHours,
            }
            : new AppSettings(),
    };
}
