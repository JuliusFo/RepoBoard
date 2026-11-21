namespace RepoBoard.Core.Repos;

/// <summary>
/// Finds repositories below a folder (e.g. C:\Dev-Repos) without descending into one it found. Each one found is a folder
/// the user's git runs in from then on (status, hooks and settings of the repository): scan only folders the user chose.
/// </summary>
public static class RepoScanner
{
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", "packages", ".vs", ".idea", "dist", "build", "target",
    };

    public static IReadOnlyList<string> Find(string root, int maxDepth = 3)
    {
        var found = new List<string>();
        Walk(new DirectoryInfo(root), 0);
        return found;

        void Walk(DirectoryInfo dir, int depth)
        {
            var dotGit = Path.Combine(dir.FullName, ".git");
            if (Path.Exists(dotGit))
            {
                if (!IsLinkedWorktree(dotGit))
                {
                    found.Add(dir.FullName);
                }

                return;
            }

            if (depth >= maxDepth)
            {
                return;
            }

            IEnumerable<DirectoryInfo> children;
            try
            {
                children = dir.EnumerateDirectories();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                return;
            }

            foreach (var child in children)
            {
                if (child.Name.StartsWith('.') || Skipped.Contains(child.Name)
                    || child.Attributes.HasFlag(FileAttributes.ReparsePoint) || child.Attributes.HasFlag(FileAttributes.System))
                {
                    continue;
                }

                Walk(child, depth + 1);
            }
        }
    }

    /// <summary>
    /// A linked worktree shows under its repository (WP-04), not on its own: its <c>.git</c> is a file pointing to a folder
    /// in the repository's git folder that holds a <c>commondir</c> file. A submodule's <c>.git</c> file points to a folder
    /// without one.
    /// </summary>
    internal static bool IsLinkedWorktree(string dotGit)
    {
        if (!File.Exists(dotGit))
        {
            return false;
        }

        try
        {
            using var reader = new StreamReader(dotGit);
            var line = reader.ReadLine();
            if (line is null || !line.StartsWith("gitdir:", StringComparison.Ordinal))
            {
                return false;
            }

            var gitDir = Path.GetFullPath(line["gitdir:".Length..].Trim(), Path.GetDirectoryName(dotGit)!);
            return File.Exists(Path.Combine(gitDir, "commondir"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
