using System.Globalization;

namespace RepoBoard.Core;

/// <summary>
/// The options RepoBoard takes on its command line (<c>--name=value</c>, for tests, screenshots and separate profiles) and
/// one way to read them (FerretSharp R5b: five copies of this had drifted apart).
/// </summary>
public static class CommandLine
{
    /// <summary>The data folder (repository list, logs, instance lock).</summary>
    public const string DataDir = "--data-dir=";

    /// <summary><c>dark</c> or <c>light</c> for this session.</summary>
    public const string Theme = "--theme=";

    /// <summary><c>en</c> or <c>de</c> for this session.</summary>
    public const string Language = "--lang=";

    /// <summary>Start in the notification area without showing the window (start with Windows, ADR 0006).</summary>
    public const string Minimized = "--minimized";

    /// <summary>Ends the instance running on the data folder (as the tray's Exit) instead of starting one.</summary>
    public const string Exit = "--exit";

    /// <summary>
    /// A folder with update packages to read instead of the GitHub releases (WP-10): only for testing an update without
    /// GitHub, not advertised.
    /// </summary>
    public const string UpdateSource = "--update-source=";

    /// <summary>Options whose value is a path: made full by <see cref="Restart"/>.</summary>
    private static readonly string[] PathOptions = [DataDir, UpdateSource];

    /// <summary>
    /// The arguments to start RepoBoard again after an update (WP-10): as it was started, but with full paths – the updater
    /// may start it in another working folder, and a relative <c>--data-dir</c> would point at an empty one (FerretSharp
    /// review 3.31.0, A44) – and with the window: the restart follows a click, so neither <see cref="Minimized"/> nor
    /// <see cref="Exit"/>.
    /// </summary>
    public static IReadOnlyList<string> Restart(IEnumerable<string> args) =>
    [
        .. args.Where(a => !string.Equals(a, Minimized, StringComparison.OrdinalIgnoreCase) && !string.Equals(a, Exit, StringComparison.OrdinalIgnoreCase))
            .Select(WithFullPath),
    ];

    private static string WithFullPath(string argument) =>
        PathOptions.FirstOrDefault(p => argument.StartsWith(p, StringComparison.OrdinalIgnoreCase)) is { } prefix
        && argument.Length > prefix.Length
            ? prefix + Path.GetFullPath(argument[prefix.Length..])
            : argument;

    /// <summary>Whether the switch <paramref name="option"/> (no value) is given.</summary>
    public static bool Has(IEnumerable<string> args, string option) =>
        args.Any(a => string.Equals(a, option, StringComparison.OrdinalIgnoreCase));

    /// <summary>The value of the first <paramref name="option"/>; null if it is not given.</summary>
    public static string? ValueOf(IEnumerable<string> args, string option) =>
        args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase))?[option.Length..];

    /// <summary>
    /// The UI language (ADR 0005): <c>--lang</c>, otherwise German on a German Windows and English everywhere else.
    /// </summary>
    public static CultureInfo UiCulture(IEnumerable<string> args, CultureInfo windows) =>
        (ValueOf(args, Language)?.ToLowerInvariant() ?? windows.TwoLetterISOLanguageName) == "de"
            ? CultureInfo.GetCultureInfo("de")
            : CultureInfo.GetCultureInfo("en");
}
