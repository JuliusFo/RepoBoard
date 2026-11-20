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
