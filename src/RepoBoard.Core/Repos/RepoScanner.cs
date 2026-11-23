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
            runs.AddRange(FolderRuns(gitDir!, common));
            runs.AddRange(SubmoduleRuns(repo, common, 0));
        }

        var outside = !IO.Paths.IsUnder(IO.Paths.Full(board), IO.Paths.Full(scanRoot));
        return new ScanCandidate(repo, board, outside, [.. runs.Distinct().Order()]);
    }

    /// <summary>What the settings and hooks of one git folder run (a linked worktree's: its own and its repository's).</summary>
    private static IEnumerable<RepoRuns> FolderRuns(string gitDir, string common)
    {
        // config.worktree too: with extensions.worktreeConfig git reads it on top (shared and per worktree).
        foreach (var runs in ConfigRuns(System.IO.Path.Combine(common, "config")))
        {
            yield return runs;
        }

        foreach (var runs in ConfigRuns(System.IO.Path.Combine(common, "config.worktree")))
        {
            yield return runs;
        }

        if (!IO.Paths.Same(gitDir, common))
        {
            foreach (var runs in ConfigRuns(System.IO.Path.Combine(gitDir, "config.worktree")))
            {
                yield return runs;
            }
        }

        if (HasOwnHooks(System.IO.Path.Combine(common, "hooks")))
        {
            yield return RepoRuns.Hooks;
        }
    }

    private const int MaxSubmoduleDepth = 4;

    /// <summary>
    /// What the submodules' git folders run: <c>git status</c> starts a git of its own in each checked-out submodule, with
    /// that submodule's settings (an fsmonitor there runs as well; second review pass R1, code review). A submodule's git
    /// folder lies in <c>&lt;git folder&gt;/modules/&lt;name&gt;</c> (names may contain slashes, a submodule's own
    /// submodules lie in its <c>modules</c>), an old one in the submodule's folder (<c>.gitmodules</c> names the path).
    /// </summary>
    private static IEnumerable<RepoRuns> SubmoduleRuns(string repo, string common, int depth)
    {
        if (depth >= MaxSubmoduleDepth)
        {
            yield break;
        }

        foreach (var module in GitFolders(System.IO.Path.Combine(common, "modules"), 0))
        {
            foreach (var runs in FolderRuns(module, module))
            {
                yield return runs;
            }
        }

        foreach (var path in SubmodulePaths(repo))
        {
            var folder = IO.Paths.Full(System.IO.Path.Combine(repo, path));
            // A path from a file in the repository: only folders inside it, and no link to elsewhere.
            if (!IO.Paths.IsUnder(folder, IO.Paths.Full(repo)) || IO.Paths.Same(folder, repo) || !IsPlainFolder(folder))
            {
                continue;
            }

            var gitDir = GitDirOf(folder);
            if (gitDir is null)
            {
                continue;
            }

            if (IO.Paths.IsShare(gitDir) && !IO.Paths.IsShare(repo))
            {
                yield return RepoRuns.GitDirOnShare;
                continue;
            }

            // A .git file naming a folder in modules was read above; an old submodule keeps its .git folder.
            if (IO.Paths.IsUnder(IO.Paths.Full(gitDir), IO.Paths.Full(System.IO.Path.Combine(common, "modules"))))
            {
                continue;
            }

            foreach (var runs in FolderRuns(gitDir, gitDir).Concat(SubmoduleRuns(folder, gitDir, depth + 1)))
            {
                yield return runs;
            }
        }
    }

    /// <summary>The git folders below <paramref name="modules"/>: a folder with a <c>config</c>, then its own <c>modules</c>.</summary>
    private static IEnumerable<string> GitFolders(string modules, int depth)
    {
        if (depth >= 2 * MaxSubmoduleDepth || !IsPlainFolder(modules))
        {
            yield break;
        }

        string[] children;
        try
        {
            children = Directory.GetDirectories(modules);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var child in children)
        {
            if (!IsPlainFolder(child))
            {
                continue;
            }

            if (File.Exists(System.IO.Path.Combine(child, "config")))
            {
                yield return child;
                foreach (var nested in GitFolders(System.IO.Path.Combine(child, "modules"), depth + 1))
                {
                    yield return nested;
                }
            }
            else
            {
                // A name with a slash: modules/lib/foo.
                foreach (var nested in GitFolders(child, depth + 1))
                {
                    yield return nested;
                }
            }
        }
    }

    /// <summary>The <c>path</c> entries of <c>.gitmodules</c>.</summary>
    private static IEnumerable<string> SubmodulePaths(string repo)
    {
        string[] lines;
        try
        {
            var file = System.IO.Path.Combine(repo, ".gitmodules");
            lines = File.Exists(file) ? File.ReadAllLines(file) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var line in lines)
        {
            var parts = line.Split('=', 2);
            if (parts.Length == 2 && parts[0].Trim().Equals("path", StringComparison.OrdinalIgnoreCase)
                && parts[1].Trim().Trim('"') is { Length: > 0 } path)
            {
                yield return path;
            }
        }
    }

    /// <summary>A folder that is no link: a link may lead to a share, which reading would sign in to.</summary>
    private static bool IsPlainFolder(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            return info.Exists && !info.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? GitDirOf(string repo)
    {
        var dotGit = System.IO.Path.Combine(repo, ".git");
        // A .git that is a link (symbolic link, junction): its target, without following it – one on a share is named
        // without reading it, like a .git file naming one (code review R1; Directory.Exists followed it, and a link that
        // led nowhere left the repository without any hint).
        if (LinkTargetOf(dotGit) is { } target && IO.Paths.IsShare(target))
        {
            return target;
        }

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

    /// <summary>
    /// Where the link at <paramref name="path"/> ends; null for no link. Link by link and only on this computer:
    /// <c>ResolveLinkTarget</c> would open the last target, and opening one on a share signs in to it.
    /// </summary>
    private static string? LinkTargetOf(string path)
    {
        try
        {
            string? target = null;
            for (var hop = 0; hop < 8 && (target is null || !IO.Paths.IsShare(target)); hop++)
            {
                var link = target ?? path;
                if (new FileInfo(link).LinkTarget is not { } next)
                {
                    break;
                }

                // As written when absolute: under Linux GetFullPath makes //host/share into /host/share.
                target = System.IO.Path.IsPathFullyQualified(next) ? next : System.IO.Path.GetFullPath(next, System.IO.Path.GetDirectoryName(link)!);
            }

            return target;
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
            // git writes it for every submodule (../../../lib); one on a share would read each status there.
            "worktree" => IsShareUrl(value) ? RepoRuns.OtherSettings : null,
            _ => HarmlessCore.Contains(name) ? null : RepoRuns.OtherSettings,
        },
        "remote" => name switch
        {
            // ext:: and fd:: run a command as the transport; upload-pack/receive-pack name the program on the other side.
            // A remote on another computer's share: each fetch signs in to that host (second review pass R1, code review).
            "url" or "pushurl" => value.Contains("::", StringComparison.Ordinal) || IsShareUrl(value) ? RepoRuns.OtherSettings : null,
            _ => HarmlessRemote.Contains(name) ? null : RepoRuns.OtherSettings,
        },
        "submodule" => name == "update" && value.StartsWith('!') ? RepoRuns.OtherSettings : null,
        // Git LFS runs on fetch and fast-forward: a custom transfer agent ([lfs "customtransfer.x"] path), an extension
        // ([lfs "extension.x"] clean/smudge) and lfs.standalonetransferagent start programs – only known keys are harmless.
        "lfs" => name switch
        {
            "url" or "pushurl" => IsShareUrl(value) ? RepoRuns.OtherSettings : null,
            _ => HarmlessLfs.Contains(name) ? null : RepoRuns.OtherSettings,
        },
        "extensions" or "branch" or "user" or "gc" or "pull" or "push" or "fetch" or "http" or "color" or "advice"
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

    /// <summary>
    /// A path or <c>file://</c> URL on another computer's share; <c>file:///</c>, <c>file://localhost/</c> and a drive
    /// (<c>file://C:/</c>) are this computer's.
    /// </summary>
    private static bool IsShareUrl(string value)
    {
        if (IO.Paths.IsShare(value))
        {
            return true;
        }

        if (!value.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = value["file://".Length..];
        return rest.Length > 0 && rest[0] != '/' && !rest.StartsWith("localhost/", StringComparison.OrdinalIgnoreCase)
            && !(rest.Length > 1 && char.IsAsciiLetter(rest[0]) && rest[1] == ':');
    }

    // [lfs "<url>"] keys (access, locksverify) are read with the same names.
    private static readonly HashSet<string> HarmlessLfs =
    [
        "repositoryformatversion", "fetchinclude", "fetchexclude", "fetchrecentrefsdays", "fetchrecentremoterefs",
        "fetchrecentcommitsdays", "fetchrecentalways", "pruneoffsetdays", "pruneremotetocheck", "pruneverifyremotealways",
        "concurrenttransfers", "basictransfersonly", "tustransfers", "allowincompletepush", "locksverify", "setlockablereadonly",
        "lockignoredfiles", "skipdownloaderrors", "storage", "dialtimeout", "tlstimeout", "activitytimeout", "keepalive",
        "cachecredentials", "access", "contenttype", "batch", "sshtransfer",
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
