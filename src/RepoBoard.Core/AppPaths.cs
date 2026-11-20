namespace RepoBoard.Core;

/// <summary>Well-known locations for per-user application data (repository list, logs, instance lock).</summary>
public sealed class AppPaths
{
    public const string AppFolderName = "RepoBoard";

    public AppPaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = root;
        Id = Hash(root);
    }

    /// <summary>%APPDATA%\RepoBoard on Windows, ~/.config/RepoBoard on Linux; <c>--data-dir</c> replaces it (tests, E2E).</summary>
    public static AppPaths Default { get; } =
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName));

    public string Root { get; }

    /// <summary>
    /// Short and stable for one data folder (case and trailing separator do not count): names what belongs to one instance
    /// outside the folder – the signal to show its window, its start-with-Windows entry.
    /// </summary>
    public string Id { get; }

    /// <summary>Whether this is the user's own data folder, not one given by <c>--data-dir</c> (tests, E2E, profiles).</summary>
    public bool IsDefault => string.Equals(Id, Default.Id, StringComparison.Ordinal);

    public string LogsDirectory => Path.Combine(Root, "logs");

    /// <summary>The repositories on the board, their auto-sync switches and the interval.</summary>
    public string ConfigFile => Path.Combine(Root, "config.json");

    /// <summary>Held open exclusively while an instance runs on this data folder.</summary>
    public string InstanceLockFile => Path.Combine(Root, "instance.lock");

    /// <summary>The process id of the instance holding <see cref="InstanceLockFile"/>.</summary>
    public string InstanceProcessFile => Path.Combine(Root, "instance.pid");

    private static string Hash(string root)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)).ToUpperInvariant();
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes, 0, 6).ToLowerInvariant();
    }
}
