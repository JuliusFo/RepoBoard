using System.Globalization;

namespace RepoBoard.Core.Git;

/// <summary>A file a commit changed, from <c>git show --name-status</c>.</summary>
/// <param name="Status">git's letter: A added, M modified, D deleted, R renamed, C copied, T type changed.</param>
/// <param name="OriginalPath">For a rename or copy: the path it came from.</param>
public sealed record CommitFile(string Path, string? OriginalPath, char Status);

/// <summary>
/// One commit with its whole message and the files it changed (WP-09). A merge commit's files are its changes against the
/// first parent – what the merge brought into the branch.
/// </summary>
/// <param name="InHead">The commit is in the history of HEAD: it can be reverted, not cherry-picked.</param>
/// <param name="Applied">Not in HEAD, but HEAD has a commit with the same change (an earlier cherry-pick): picking it again
/// would come out empty.</param>
public sealed record CommitDetails(
    string Id,
    string ShortId,
    IReadOnlyList<string> Parents,
    string Author,
    string AuthorEmail,
    DateTimeOffset AuthorDate,
    string Committer,
    DateTimeOffset CommitDate,
    string Refs,
    string Message,
    IReadOnlyList<CommitFile> Files,
    bool InHead = false,
    bool Applied = false)
{
    public bool IsMerge => Parents.Count > 1;
    public bool IsRoot => Parents.Count == 0;

    /// <summary>The first line of the message.</summary>
    public string Subject => Message.Split('\n', 2)[0].Trim();

    /// <summary>The git format of <see cref="Parse"/>: fields split by <c>%x1f</c>, the message last (it may hold anything else).</summary>
    internal const string Format = "--format=%H%x1f%h%x1f%P%x1f%an%x1f%ae%x1f%aI%x1f%cn%x1f%cI%x1f%D%x1f%B";

    /// <summary>The commit from <see cref="Format"/> and the files from <c>--name-status -z</c>; null if the output is not one.</summary>
    internal static CommitDetails? Parse(string header, string files)
    {
        var parts = header.Split('\x1f', 10);
        if (parts.Length < 10
            || !DateTimeOffset.TryParse(parts[5], CultureInfo.InvariantCulture, DateTimeStyles.None, out var authored)
            || !DateTimeOffset.TryParse(parts[7], CultureInfo.InvariantCulture, DateTimeStyles.None, out var committed))
        {
            return null;
        }

        return new CommitDetails(parts[0], parts[1], parts[2].Split(' ', StringSplitOptions.RemoveEmptyEntries), parts[3], parts[4],
            authored, parts[6], committed, parts[8], parts[9].TrimEnd(), ParseFiles(files));
    }

    /// <summary>
    /// <c>--name-status -z</c>: the letter (with a score for R and C) and the path, each ended by NUL; a rename or copy has
    /// the original path before the new one.
    /// </summary>
    internal static IReadOnlyList<CommitFile> ParseFiles(string output)
    {
        var fields = output.Split('\0');
        var files = new List<CommitFile>();
        var i = 0;
        while (i < fields.Length)
        {
            // "--format=" still leaves a newline before the first entry.
            var status = fields[i].Trim('\n', '\r');
            if (status.Length == 0)
            {
                i++;
                continue;
            }

            var letter = status[0];
            if (letter is 'R' or 'C' && i + 2 < fields.Length)
            {
                files.Add(new CommitFile(fields[i + 2], fields[i + 1], letter));
                i += 3;
            }
            else if (i + 1 < fields.Length)
            {
                files.Add(new CommitFile(fields[i + 1], null, letter));
                i += 2;
            }
            else
            {
                break;
            }
        }

        return files;
    }
}
