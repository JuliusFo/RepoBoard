using System.Text;

namespace RepoBoard.Core;

/// <summary>
/// The entry that starts RepoBoard with Windows (ADR 0006): a value under <see cref="RunKey"/> in the user's registry. The
/// host reads and writes it; what it contains is decided here, testable without Windows.
/// </summary>
public static class Autostart
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Where the Task Manager notes a start entry the user switched off (first byte odd = off).</summary>
    public const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>
    /// "RepoBoard" for the user's own data folder; another folder (<c>--data-dir</c>: E2E, a second profile) gets its own
    /// name, so it never replaces the user's entry.
    /// </summary>
    public static string ValueName(AppPaths paths) => paths.IsDefault ? AppPaths.AppFolderName : $"{AppPaths.AppFolderName} ({paths.Id})";

    /// <summary>The command line: started in the notification area (decision of the user), with the data folder if not the default.</summary>
    public static string Command(string exe, AppPaths paths)
    {
        var command = $"{Quote(exe, always: true)} {CommandLine.Minimized}";
        return paths.IsDefault ? command : $"{command} {Quote(CommandLine.DataDir + paths.Root)}";
    }

    /// <summary>On unless the Task Manager switched it off (no value there means on).</summary>
    public static bool IsApproved(byte[]? approved) => approved is not { Length: > 0 } || (approved[0] & 1) == 0;

    /// <summary>One argument as Windows splits a command line (<c>CommandLineToArgvW</c>): quotes, and backslashes before a quote doubled.</summary>
    public static string Quote(string argument, bool always = false)
    {
        if (!always && argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0)
        {
            return argument;
        }

        var quoted = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            quoted.Append('\\', c == '"' ? (backslashes * 2) + 1 : backslashes).Append(c);
            backslashes = 0;
        }

        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }
}
