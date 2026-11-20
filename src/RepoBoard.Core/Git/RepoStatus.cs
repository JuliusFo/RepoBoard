using System.Globalization;

namespace RepoBoard.Core.Git;

/// <summary>One changed file from <c>git status --porcelain=v2</c>.</summary>
/// <param name="Index">X of XY: the staged state ('.' = unchanged).</param>
/// <param name="WorkTree">Y of XY: the unstaged state ('.' = unchanged).</param>
public sealed record FileChange(string Path, string? OriginalPath, char Index, char WorkTree, bool Untracked, bool Conflict)
{
    public bool IsStaged => !Untracked && !Conflict && Index != '.';
    public bool IsUnstaged => Untracked || Conflict || WorkTree != '.';

    /// <summary>One letter for the list: the staged state, or the worktree's.</summary>
    public char Letter(bool staged) => Conflict ? 'U' : Untracked ? '?' : staged ? Index : WorkTree;
}

/// <summary>The state of one repository's worktree and branch.</summary>
public sealed record RepoStatus(
    string? Branch,
    string? Head,
    string? Upstream,
    int Ahead,
    int Behind,
    IReadOnlyList<FileChange> Changes)
{
    public bool IsDetached => Branch is null;
    public bool IsInitial => Head is null;
    public bool HasUpstream => Upstream is not null;
    public int Staged => Changes.Count(c => c.IsStaged);
    public int Modified => Changes.Count(c => !c.Untracked && !c.Conflict && c.WorkTree != '.');
    public int Untracked => Changes.Count(c => c.Untracked);
    public int Conflicts => Changes.Count(c => c.Conflict);
    public bool IsClean => Changes.Count == 0;

    /// <summary>
    /// Parses <c>git status --porcelain=v2 --branch -z</c>. Entries end with NUL; a rename (type 2) has its original path
    /// as the next NUL-separated field.
    /// </summary>
    public static RepoStatus Parse(string output)
    {
        string? branch = null, head = null, upstream = null;
        int ahead = 0, behind = 0;
        var changes = new List<FileChange>();
        var fields = output.Split('\0');
        for (var i = 0; i < fields.Length; i++)
        {
            var field = fields[i];
            if (field.Length < 2)
            {
                continue;
            }

            switch (field[0])
            {
                case '#':
                    ParseHeader(field, ref branch, ref head, ref upstream, ref ahead, ref behind);
                    break;
                case '1':
                {
                    // 1 XY sub mH mI mW hH hI path
                    var parts = field.Split(' ', 9);
                    changes.Add(new FileChange(parts[8], null, parts[1][0], parts[1][1], false, false));
                    break;
                }

                case '2':
                {
                    // 2 XY sub mH mI mW hH hI Xscore path NUL origPath
                    var parts = field.Split(' ', 10);
                    var original = i + 1 < fields.Length ? fields[++i] : null;
                    changes.Add(new FileChange(parts[9], original, parts[1][0], parts[1][1], false, false));
                    break;
                }

                case 'u':
                {
                    // u XY sub m1 m2 m3 mW h1 h2 h3 path
                    var parts = field.Split(' ', 11);
                    changes.Add(new FileChange(parts[10], null, parts[1][0], parts[1][1], false, true));
                    break;
                }

                case '?':
                    changes.Add(new FileChange(field[2..], null, '.', '?', true, false));
                    break;
            }
        }

        return new RepoStatus(branch, head, upstream, ahead, behind, changes);
    }

    private static void ParseHeader(string line, ref string? branch, ref string? head, ref string? upstream, ref int ahead, ref int behind)
    {
        var parts = line.Split(' ');
        if (parts.Length < 3)
        {
            return;
        }

        switch (parts[1])
        {
            case "branch.oid":
                head = parts[2] == "(initial)" ? null : parts[2];
                break;
            case "branch.head":
                branch = parts[2] == "(detached)" ? null : parts[2];
                break;
            case "branch.upstream":
                upstream = parts[2];
                break;
            case "branch.ab" when parts.Length >= 4:
                ahead = int.Parse(parts[2].TrimStart('+'), CultureInfo.InvariantCulture);
                behind = int.Parse(parts[3].TrimStart('-'), CultureInfo.InvariantCulture);
                break;
        }
    }
}

/// <summary>A local branch, or a remote branch without a local one.</summary>
public sealed record BranchInfo(string Name, bool IsCurrent, bool IsRemote, string? Upstream, string? Track, string? LastCommit);

/// <summary>A commit of the recent history.</summary>
public sealed record CommitInfo(string Hash, string Author, string When, string Refs, string Subject);

/// <summary>What the expanded row shows besides the status.</summary>
public sealed record RepoDetails(IReadOnlyList<BranchInfo> Branches, IReadOnlyList<CommitInfo> Log);
