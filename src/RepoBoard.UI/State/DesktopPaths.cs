namespace RepoBoard.UI.State;

/// <summary>
/// The host's decisions about paths that can be made – and tested – without the host (review 0.4.0, A34, A35, C9): where
/// the folder dialog starts, whether a path can go through <c>cmd /c</c>, and where a command lies on the PATH.
/// </summary>
public static class DesktopPaths
{
    /// <summary>
    /// Where "add" and "scan" start: the folder all repositories on the board share (where the user keeps them); null
    /// without one – the host takes the profile. Never a fixed path: that was one developer's folder (review 0.4.0, A35).
    /// </summary>
    public static string? StartFolder(IEnumerable<string> repositories)
    {
        string? common = null;
        foreach (var repo in repositories)
        {
            var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(repo));
            if (parent is null)
            {
                continue;
            }

            common = common is null ? parent : Common(common, parent);
            if (common is null)
            {
                return null;
            }
        }

        return common;

        static string? Common(string a, string b)
        {
            for (var folder = a; folder is not null; folder = Path.GetDirectoryName(folder))
            {
                if (Core.IO.Paths.IsUnder(b, folder))
                {
                    return folder;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// A path <c>cmd /c</c> passes on as it is: cmd reads <c>&amp;</c>, <c>|</c>, <c>&lt;</c>, <c>&gt;</c>, <c>^</c>, <c>%</c> and
    /// quotes itself, and a folder name could carry a command.
    /// </summary>
    public static bool SafeForCmd(string path) => path.IndexOfAny(['"', '&', '|', '<', '>', '^', '%']) < 0;

    /// <summary>
    /// The file a command name starts from the PATH (<paramref name="command"/> with each extension of PATHEXT); null if it
    /// is on none – "In VS Code" did nothing at all without one (review 0.4.0, A34).
    /// </summary>
    public static string? FindOnPath(string command, string? path, string? pathExtensions)
    {
        string[] extensions = OperatingSystem.IsWindows()
            ? [.. (pathExtensions ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries)]
            : [""];
        foreach (var folder in (path ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(folder.Trim('"'), command + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
