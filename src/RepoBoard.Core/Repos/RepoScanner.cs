namespace RepoBoard.Core.Repos;

/// <summary>
/// Finds repositories below a folder without descending into one it found. Each one found becomes a folder the user's git
/// runs in (status, hooks and settings of the repository), so nothing here starts git: <see cref="Inspect"/> reads what
/// the repository's configuration would run, and the user ticks which to add (review 0.4.0, A9). A linked worktree is
/// found like a repository; its main worktree goes on the board instead (WP-04) – when that lies outside the scanned
/// folder, the list says so.
/// </summary>
public static class RepoScanner
{
    /// <summary>The repositories below <paramref name="root"/>, each read as <see cref="Inspect"/> does.</summary>
    public static IReadOnlyList<ScanCandidate> Candidates(string root, int maxDepth = 3) =>
        [.. Find(root, maxDepth).Select(path => Inspect(path, root))];

    /// <summary>
    /// What <paramref name="repo"/> would make git run, read from its git folder without git: <c>.git</c> as a folder, or as
    /// a file naming one (<c>gitdir: …</c>); a linked worktree's settings and hooks are its repository's
    /// (<c>commondir</c>).
    /// </summary>
    public static ScanCandidate Inspect(string repo, string scanRoot)
    {
        var gitDir = GitDirOf(repo);
        var common = gitDir is null ? null : CommonDirOf(gitDir);
        var board = repo;
        // A linked worktree: its git folder lies in <repository>/.git/worktrees/<name>, the repository is the common folder's parent.
        if (gitDir is not null && common is not null && !IO.Paths.Same(gitDir, common)
            && System.IO.Path.GetFileName(IO.Paths.Full(common)) == ".git" && System.IO.Path.GetDirectoryName(IO.Paths.Full(common)) is { } main)
        {
            board = main;
        }

        var runs = new List<RepoRuns>();
        if (common is not null)
        {
            runs.AddRange(ConfigRuns(System.IO.Path.Combine(common, "config")));
            if (HasOwnHooks(System.IO.Path.Combine(common, "hooks")))
            {
                runs.Add(RepoRuns.Hooks);
            }
        }

        var outside = !IO.Paths.IsUnder(IO.Paths.Full(board), IO.Paths.Full(scanRoot));
        return new ScanCandidate(repo, board, outside, [.. runs.Distinct().Order()]);
    }

    private static string? GitDirOf(string repo)
    {
        var dotGit = System.IO.Path.Combine(repo, ".git");
        if (Directory.Exists(dotGit))
        {
            return dotGit;
        }

        try
        {
            var line = File.ReadLines(dotGit).FirstOrDefault()?.Trim();
            return line is not null && line.StartsWith("gitdir:", StringComparison.Ordinal)
                ? System.IO.Path.GetFullPath(System.IO.Path.Combine(repo, line["gitdir:".Length..].Trim()))
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? CommonDirOf(string gitDir)
    {
        try
        {
            var file = System.IO.Path.Combine(gitDir, "commondir");
            return File.Exists(file)
                ? System.IO.Path.GetFullPath(System.IO.Path.Combine(gitDir, File.ReadAllText(file).Trim()))
                : gitDir;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return gitDir;
        }
    }

    /// <summary>The keys of git's own config format that run a program; sections and keys are case-insensitive.</summary>
    internal static IEnumerable<RepoRuns> ConfigRuns(string configFile)
    {
        string[] lines;
        try
        {
            lines = File.Exists(configFile) ? File.ReadAllLines(configFile) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        var section = "";
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                var end = line.IndexOf(']', StringComparison.Ordinal);
                section = (end > 0 ? line[1..end] : line[1..]).Trim();
                line = end > 0 ? line[(end + 1)..].Trim() : "";
                if (section.StartsWith("include", StringComparison.OrdinalIgnoreCase))
                {
                    yield return RepoRuns.Includes;
                }
            }

            var name = line.Split('=', 2)[0].Trim();
            if (name.Length == 0 || name.StartsWith('#') || name.StartsWith(';') || !section.Equals("core", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (name.Equals("fsmonitor", StringComparison.OrdinalIgnoreCase) && RunsProgram(line))
            {
                yield return RepoRuns.FsMonitor;
            }
            else if (name.Equals("hooksPath", StringComparison.OrdinalIgnoreCase))
            {
                yield return RepoRuns.HooksPath;
            }
            else if (name.Equals("sshCommand", StringComparison.OrdinalIgnoreCase))
            {
                yield return RepoRuns.SshCommand;
            }
        }

        // A boolean runs nothing of the repository's: false is off, true (or the bare key) git's own daemon. Anything else is
        // a program git starts.
        static bool RunsProgram(string line) =>
            line.Split('=', 2) is [_, var value]
            && value.Trim().Trim('"').ToLowerInvariant() is not ("false" or "no" or "off" or "0" or "true" or "yes" or "on" or "1" or "");
    }

    private static bool HasOwnHooks(string hooks)
    {
        try
        {
            return Directory.Exists(hooks)
                && Directory.EnumerateFiles(hooks).Any(f => !f.EndsWith(".sample", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

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
