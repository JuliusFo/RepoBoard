namespace RepoBoard.Core;

/// <summary>Well-known locations for per-user application data (repository list, logs, instance lock).</summary>
public sealed class AppPaths
{
    public const string AppFolderName = "RepoBoard";

    public AppPaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = root;
    }

    /// <summary>%APPDATA%\RepoBoard on Windows, ~/.config/RepoBoard on Linux; <c>--data-dir</c> replaces it (tests, E2E).</summary>
    public static AppPaths Default { get; } =
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName));

    public string Root { get; }

    public string LogsDirectory => Path.Combine(Root, "logs");

    /// <summary>The repositories on the board, their auto-sync switches and the interval.</summary>
    public string ConfigFile => Path.Combine(Root, "config.json");

    /// <summary>Held open exclusively while an instance runs on this data folder.</summary>
    public string InstanceLockFile => Path.Combine(Root, "instance.lock");

    /// <summary>The process id of the instance holding <see cref="InstanceLockFile"/>.</summary>
    public string InstanceProcessFile => Path.Combine(Root, "instance.pid");
}
