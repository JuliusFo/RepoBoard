namespace RepoBoard.Core.Repos;

/// <summary>
/// Finds repositories below a folder (e.g. C:\Dev-Repos) without descending into one it found. Each one found is a folder
/// the user's git runs in from then on (status, hooks and settings of the repository): scan only folders the user chose.
/// A linked worktree is found like a repository; <c>BoardService.AddAsync</c> puts its repository on the board instead
/// (WP-04) – also when that repository lies outside the scanned folder (review WP-04).
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
            if (Path.Exists(Path.Combine(dir.FullName, ".git")))
            {
                found.Add(dir.FullName);
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
}
