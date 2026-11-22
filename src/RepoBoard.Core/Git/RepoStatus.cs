using System.Globalization;

namespace RepoBoard.Core.Git;

/// <summary>One changed file from <c>git status --porcelain=v2</c>.</summary>
/// <param name="Index">X of XY: the staged state ('.' = unchanged).</param>
/// <param name="WorkTree">Y of XY: the unstaged state ('.' = unchanged).</param>
public sealed record FileChange(string Path, string? OriginalPath, char Index, char WorkTree, bool Untracked, bool Conflict)
{
    public bool IsStaged => !Untracked && !Conflict && Index != '.';
    public bool IsUnstaged => Untracked || Conflict || WorkTree != '.';

    /// <summary>
    /// An untracked folder that git lists as a whole (<c>sub/</c>) even with <c>--untracked-files=all</c>: a repository of
    /// its own inside the worktree. It has no diff (review WP-05).
    /// </summary>
    public bool IsNestedRepository => Untracked && Path.EndsWith('/');

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
    IReadOnlyList<FileChange> Changes,
    bool UpstreamGone = false)
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
        var compared = false;
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
                    compared |= ParseHeader(field, ref branch, ref head, ref upstream, ref ahead, ref behind);
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

        // An upstream git names but cannot compare with is gone on the remote (deleted after its pull request): "up to date"
        // would be wrong (E2E WP-04). Not without a commit of our own: git compares nothing then (review 0.4.0, A18).
        return new RepoStatus(branch, head, upstream, ahead, behind, changes, upstream is not null && head is not null && !compared);
    }

    /// <summary>One header line; true for <c>branch.ab</c>, which git writes only when the upstream exists.</summary>
    private static bool ParseHeader(string line, ref string? branch, ref string? head, ref string? upstream, ref int ahead, ref int behind)
    {
        var parts = line.Split(' ');
        if (parts.Length < 3)
        {
            return false;
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
                return true;
        }

        return false;
    }
}
