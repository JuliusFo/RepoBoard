namespace RepoBoard.Core.IO;

/// <summary>
/// One answer to "the same path?" and "below that folder?" (review 0.4.0, B3): six copies with different rules had drifted
/// apart – under Linux the board took <c>/r/App</c> and <c>/r/app</c> as two repositories and the config dropped one.
/// Case-insensitive on Windows, case-sensitive elsewhere.
/// </summary>
public static class Paths
{
    public static StringComparison Comparison { get; } =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static StringComparer Comparer { get; } = StringComparer.FromComparison(Comparison);

    /// <summary>The full path without a separator at the end (a drive root keeps its own).</summary>
    public static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>The same folder or file, however written (<c>C:/x</c>, <c>C:\a\..\x\</c>).</summary>
    public static bool Same(string a, string b) => string.Equals(Full(a), Full(b), Comparison);

    /// <summary>A path on another computer's share (<c>\\host\share</c>, also written with forward slashes).</summary>
    public static bool IsShare(string path) => path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal);

    /// <summary>
    /// <paramref name="path"/> is <paramref name="folder"/> or lies below it. Both already full and normalized – this only
    /// compares (the file watcher asks it for every event).
    /// </summary>
    public static bool IsUnder(string path, string folder) =>
        path.StartsWith(folder, Comparison)
        && (path.Length == folder.Length || path[folder.Length] == Path.DirectorySeparatorChar || Path.EndsInDirectorySeparator(folder));
}
