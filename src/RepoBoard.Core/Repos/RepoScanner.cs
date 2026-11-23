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

        // A folder on the way to them that links to a share (gitdir: sub/link/real): the same, unread (fourth review pass R1).
        if (!IO.Paths.IsShare(repo) && (LinkedToShareOnTheWayFrom(repo, gitDir) || LinkedToShareOnTheWayFrom(repo, common)))
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

    /// <summary>
    /// What the settings and hooks of one git folder run (a linked worktree's: its own and its repository's). A folder or file
    /// in it that is a link to another computer's share is named, not read (third review pass R1).
    /// </summary>
    private static IEnumerable<RepoRuns> FolderRuns(string gitDir, string common)
    {
        if (LinkedToShare(gitDir) || LinkedToShare(common))
        {
            yield return RepoRuns.GitDirOnShare;
            yield break;
        }

        // config.worktree too: with extensions.worktreeConfig git reads it on top (shared and per worktree).
        string[] configs = IO.Paths.Same(gitDir, common)
            ? [System.IO.Path.Combine(common, "config"), System.IO.Path.Combine(common, "config.worktree")]
            : [System.IO.Path.Combine(common, "config"), System.IO.Path.Combine(common, "config.worktree"), System.IO.Path.Combine(gitDir, "config.worktree")];
        foreach (var config in configs)
        {
            if (LinkedToShare(config))
            {
                yield return RepoRuns.GitDirOnShare;
                continue;
            }

            foreach (var runs in ConfigRuns(config))
            {
                yield return runs;
            }
        }

        var hooks = System.IO.Path.Combine(common, "hooks");
        if (LinkedToShare(hooks))
        {
            yield return RepoRuns.GitDirOnShare;
        }
        else if (HasOwnHooks(hooks))
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
    /// A link on the way is followed like git does – unless it leads to another computer's share.
    /// </summary>
    private static IEnumerable<RepoRuns> SubmoduleRuns(string repo, string common, int depth)
    {
        if (depth >= MaxSubmoduleDepth)
        {
            yield break;
        }

        var budget = new WalkBudget();
        foreach (var module in GitFolders(System.IO.Path.Combine(common, "modules"), 0, budget))
        {
            foreach (var runs in FolderRuns(module, module))
            {
                yield return runs;
            }
        }

        // More than a repository's submodules ever hold – links in a loop or to a disk: not judged, so not harmless.
        if (budget.Exhausted)
        {
            yield return RepoRuns.OtherSettings;
        }

        var gitmodules = System.IO.Path.Combine(repo, ".gitmodules");
        if (LinkedToShare(gitmodules))
        {
            yield return RepoRuns.GitDirOnShare;
            yield break;
        }

        foreach (var path in SubmodulePaths(gitmodules))
        {
            var folder = IO.Paths.Full(System.IO.Path.Combine(repo, path));
            // A path from a file in the repository: only folders inside it.
            if (!IO.Paths.IsUnder(folder, IO.Paths.Full(repo)) || IO.Paths.Same(folder, repo))
            {
                continue;
            }

            if (LinkedToShareOnTheWay(IO.Paths.Full(repo), folder))
            {
                yield return RepoRuns.GitDirOnShare;
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

    /// <summary>How many entries below <c>modules</c> a scan looks at: local links are followed, as git does.</summary>
    private sealed class WalkBudget
    {
        private int _left = 1000;

        public bool Exhausted { get; private set; }

        public bool Take()
        {
            Exhausted |= --_left < 0;
            return !Exhausted;
        }
    }

    /// <summary>
    /// The git folders below <paramref name="modules"/>: a folder with a <c>config</c>, then its own <c>modules</c>. A link to
    /// a share comes back unread – <see cref="FolderRuns"/> names it. Every entry, not only folders: under Linux a link
    /// whose target is not there (<c>//host/share</c> is a local path there) is no folder for <c>GetDirectories</c>.
    /// </summary>
    private static IEnumerable<string> GitFolders(string modules, int depth, WalkBudget budget)
    {
        if (depth >= 2 * MaxSubmoduleDepth)
        {
            yield break;
        }

        if (LinkedToShare(modules))
        {
            yield return modules;
            yield break;
        }

        string[] entries;
        try
        {
            entries = Directory.Exists(modules) ? Directory.GetFileSystemEntries(modules) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var entry in entries)
        {
            if (!budget.Take())
            {
                yield break;
            }

            if (LinkedToShare(entry))
            {
                yield return entry;
            }
            else if (!Directory.Exists(entry))
            {
                continue;
            }
            else if (File.Exists(System.IO.Path.Combine(entry, "config")))
            {
                yield return entry;
                foreach (var nested in GitFolders(System.IO.Path.Combine(entry, "modules"), depth + 1, budget))
                {
                    yield return nested;
                }
            }
            else
            {
                // A name with a slash: modules/lib/foo.
                foreach (var nested in GitFolders(entry, depth + 1, budget))
                {
                    yield return nested;
                }
            }
        }
    }

    /// <summary>The <c>path</c> entries of a <c>.gitmodules</c>.</summary>
    private static IEnumerable<string> SubmodulePaths(string gitmodules)
    {
        string[] lines;
        try
        {
            lines = File.Exists(gitmodules) ? File.ReadAllLines(gitmodules) : [];
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

    /// <summary>
    /// <paramref name="path"/> is a link (symbolic link, junction) that ends on another computer's share, while the path
    /// itself is not on one: reading through it would sign in to that host before the user ticked the repository.
    /// </summary>
    private static bool LinkedToShare(string path) =>
        !IO.Paths.IsShare(path) && LinkTargetOf(path) is { } target && IO.Paths.IsShare(target);

    /// <summary>
    /// A folder on the way to <paramref name="path"/> is a link to a share: from the repository down when the path lies in it,
    /// else from the drive (a worktree's git folder lies in its repository elsewhere).
    /// </summary>
    private static bool LinkedToShareOnTheWayFrom(string repo, string? path)
    {
        if (path is null)
        {
            return false;
        }

        var full = IO.Paths.Full(path);
        var start = IO.Paths.IsUnder(full, IO.Paths.Full(repo)) ? IO.Paths.Full(repo) : System.IO.Path.GetPathRoot(full) ?? full;
        return LinkedToShareOnTheWay(start, full);
    }

    /// <summary>A folder from <paramref name="root"/> down to <paramref name="path"/> (both full) is a link to a share.</summary>
    private static bool LinkedToShareOnTheWay(string root, string path)
    {
        for (var folder = path; folder is not null && !IO.Paths.Same(folder, root); folder = System.IO.Path.GetDirectoryName(folder))
        {
            if (LinkedToShare(folder))
            {
                return true;
            }
        }

        return false;
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
            // A commondir that is a link to a share: its target, unread – Inspect names it (third review pass R1).
            if (LinkedToShare(file))
            {
                return LinkTargetOf(file);
            }

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
            // upload-pack/receive-pack (not on the list) name the program on the other side.
            "url" or "pushurl" or "pushdefault" => RemoteRuns(value),
            _ => HarmlessRemote.Contains(name) ? null : RepoRuns.OtherSettings,
        },
        // branch.<name>.remote may be a URL instead of a remote's name: a pull goes there (third review pass R1).
        "branch" => name is "remote" or "pushremote" ? RemoteRuns(value) : null,
        "submodule" => name == "update" && value.StartsWith('!') ? RepoRuns.OtherSettings : null,
        // Git LFS runs on fetch and fast-forward: a custom transfer agent ([lfs "customtransfer.x"] path), an extension
        // ([lfs "extension.x"] clean/smudge) and lfs.standalonetransferagent start programs – only known keys are harmless.
        "lfs" => name switch
        {
            // lfs.storage on a share: every fetch and checkout reads the objects there (third review pass R1).
            "url" or "pushurl" or "storage" => IsShareUrl(value) ? RepoRuns.OtherSettings : null,
            _ => HarmlessLfs.Contains(name) ? null : RepoRuns.OtherSettings,
        },
        "extensions" or "user" or "gui" or "gc" or "pull" or "push" or "fetch" or "http" or "color" or "advice"
            or "status" or "log" or "init" or "index" or "pack" or "rerere" or "maintenance" or "receive" or "checkout"
            or "difftool" or "mergetool" => null, // the tools run only with git difftool/mergetool, which RepoBoard never calls
        "merge" or "diff" => name is "ff" or "renames" or "renamelimit" or "conflictstyle" or "tool" or "algorithm" ? null : RepoRuns.OtherSettings,
        _ => RepoRuns.OtherSettings,
    };

    /// <summary>
    /// A remote's URL (or a branch's remote, which may be one): <c>ext::</c>/<c>fd::</c> run a command as the transport, and a
    /// remote on another computer's share signs in to that host on each fetch (second review pass R1, code review).
    /// </summary>
    private static RepoRuns? RemoteRuns(string value) =>
        value.Contains("::", StringComparison.Ordinal) || IsShareUrl(value) ? RepoRuns.OtherSettings : null;

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
        "lockignoredfiles", "skipdownloaderrors", "dialtimeout", "tlstimeout", "activitytimeout", "keepalive",
        "cachecredentials", "access", "contenttype", "batch", "sshtransfer",
    ];

    private static readonly HashSet<string> HarmlessRemote =
    [
        "fetch", "push", "tagopt", "prune", "prunetags", "skipdefaultupdate", "skipfetchall", "mirror", "promisor",
        "partialclonefilter", "followremotehead",
        "gh-resolved", // GitHub CLI (gh repo set-default, gh pr checkout)
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
