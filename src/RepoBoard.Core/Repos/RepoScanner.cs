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
        // A git folder on another computer's share, named by a file in the folder: not read before the user ticks it – reading
        // sends a sign-in to that host, and what it serves can change after the scan (second review pass R1).
        if (gitDir is not null && IO.Paths.IsShare(gitDir) && !IO.Paths.IsShare(repo))
        {
            return new ScanCandidate(repo, repo, false, [RepoRuns.GitDirOnShare]);
        }

        var common = gitDir is null ? null : CommonDirOf(gitDir);
        if (common is not null && IO.Paths.IsShare(common) && !IO.Paths.IsShare(repo))
        {
            return new ScanCandidate(repo, repo, false, [RepoRuns.GitDirOnShare]);
        }

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
            // config.worktree too: with extensions.worktreeConfig git reads it on top (shared and per worktree).
            runs.AddRange(ConfigRuns(System.IO.Path.Combine(common, "config")));
            runs.AddRange(ConfigRuns(System.IO.Path.Combine(common, "config.worktree")));
            if (gitDir is not null && !IO.Paths.Same(gitDir, common))
            {
                runs.AddRange(ConfigRuns(System.IO.Path.Combine(gitDir, "config.worktree")));
            }

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

    /// <summary>
    /// What a file in git's config format makes git run; sections and keys are case-insensitive. A positive list: a setting
    /// the list does not know counts as <see cref="RepoRuns.OtherSettings"/> – a filter, a diff driver or a credential
    /// helper starts a program too, and a list of the bad ones missed them (second review pass R1).
    /// </summary>
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
                var header = (end > 0 ? line[1..end] : line[1..]).Trim();
                // [remote "origin"] and the old [branch.main]: the section is the first word.
                section = header.Split([' ', '.', '"'], 2)[0].ToLowerInvariant();
                line = end > 0 ? line[(end + 1)..].Trim() : "";
                if (section is "include" or "includeif")
                {
                    yield return RepoRuns.Includes;
                }
            }

            var parts = line.Split('=', 2);
            var name = parts[0].Trim().ToLowerInvariant();
            if (name.Length == 0 || name.StartsWith('#') || name.StartsWith(';') || section is "include" or "includeif")
            {
                continue;
            }

            var value = parts.Length > 1 ? parts[1].Trim().Trim('"') : "";
            if (Runs(section, name, value) is { } runs)
            {
                yield return runs;
            }
        }
    }

    /// <summary>What one setting runs; null for one known to run nothing of the repository's.</summary>
    private static RepoRuns? Runs(string section, string name, string value) => section switch
    {
        "core" => name switch
        {
            "fsmonitor" => IsBoolean(value) ? null : RepoRuns.FsMonitor,
            "hookspath" => RepoRuns.HooksPath,
            "sshcommand" => RepoRuns.SshCommand,
            _ => HarmlessCore.Contains(name) ? null : RepoRuns.OtherSettings,
        },
        "remote" => name switch
        {
            // ext:: and fd:: run a command as the transport; upload-pack/receive-pack name the program on the other side.
            "url" or "pushurl" => value.Contains("::", StringComparison.Ordinal) ? RepoRuns.OtherSettings : null,
            _ => HarmlessRemote.Contains(name) ? null : RepoRuns.OtherSettings,
        },
        "submodule" => name == "update" && value.StartsWith('!') ? RepoRuns.OtherSettings : null,
        "extensions" or "branch" or "user" or "lfs" or "gc" or "pull" or "push" or "fetch" or "http" or "color" or "advice"
            or "status" or "log" or "init" or "index" or "pack" or "rerere" or "maintenance" or "receive" or "checkout"
            or "difftool" or "mergetool" => null, // the tools run only with git difftool/mergetool, which RepoBoard never calls
        "merge" or "diff" => name is "ff" or "renames" or "renamelimit" or "conflictstyle" or "tool" or "algorithm" ? null : RepoRuns.OtherSettings,
        _ => RepoRuns.OtherSettings,
    };

    private static bool IsBoolean(string value) =>
        value.ToLowerInvariant() is "false" or "no" or "off" or "0" or "true" or "yes" or "on" or "1" or "";

    private static readonly HashSet<string> HarmlessCore =
    [
        "repositoryformatversion", "filemode", "bare", "logallrefupdates", "symlinks", "ignorecase", "autocrlf", "eol", "safecrlf",
        "precomposeunicode", "longpaths", "quotepath", "sparsecheckout", "sparsecheckoutcone", "fscache", "untrackedcache",
        "splitindex", "commitgraph", "multipackindex", "compression", "loosecompression", "abbrev", "preloadindex", "checkstat",
        "trustctime", "deltabasecachelimit", "bigfilethreshold", "packedgitlimit", "packedgitwindowsize", "excludesfile",
        "attributesfile", "whitespace", "warnambiguousrefs", "protecthfs", "protectntfs", "filesreflocktimeout", "hidedotfiles",
    ];

    private static readonly HashSet<string> HarmlessRemote =
    [
        "fetch", "push", "tagopt", "prune", "prunetags", "skipdefaultupdate", "skipfetchall", "mirror", "promisor",
        "partialclonefilter", "followremotehead",
    ];

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
