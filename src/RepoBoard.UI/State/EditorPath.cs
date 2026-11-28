namespace RepoBoard.UI.State;

/// <summary>Which file of a repository the host may hand to VS Code (WP-07, <see cref="IDesktop.OpenInEditor"/>).</summary>
public static class EditorPath
{
    /// <summary>
    /// The full path of <paramref name="file"/> (relative, from the status) inside <paramref name="repo"/>; null if it lies
    /// outside – <c>..</c> or a rooted path – or is the folder itself. Measured as a relative path, so a repository at a drive's
    /// root (<c>D:\</c>) works too (review WP-07).
    /// </summary>
    public static string? FileIn(string repo, string file)
    {
        var root = Path.GetFullPath(repo);
        var full = Path.GetFullPath(Path.Combine(root, file));
        var relative = Path.GetRelativePath(root, full);
        return relative == "." || relative == ".." || Path.IsPathRooted(relative)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
                ? null
                : full;
    }
}
