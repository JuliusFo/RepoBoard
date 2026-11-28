namespace RepoBoard.Core.Git;

/// <summary>
/// One entry of the stash list (WP-06, ADR 0016). The stash belongs to the repository: its worktrees share it.
/// </summary>
/// <param name="Position">Its place in the list now (<c>stash@{n}</c>); it moves when an entry above it comes or goes.</param>
/// <param name="Id">The commit of the entry – what identifies it across a change of <paramref name="Position"/>.</param>
/// <param name="Message">git's subject of the entry: "On main: message" or "WIP on main: abc1234 subject".</param>
public sealed record StashEntry(int Position, string Id, string Message, DateTimeOffset? Created)
{
    /// <summary>The ref git takes for drop and pop: <c>stash@{n}</c>.</summary>
    public string Ref => $"stash@{{{Position}}}";

    /// <summary>
    /// The entries of <c>stash list -z --format=%H%x00%ct%x00%gs</c>: three fields per entry, every field and entry ended by
    /// NUL – a subject never splits an entry (review 0.4.0, A7).
    /// </summary>
    internal static IReadOnlyList<StashEntry> ParseList(string output)
    {
        var fields = output.Split('\0');
        var entries = new List<StashEntry>();
        for (var i = 0; i + 2 < fields.Length; i += 3)
        {
            var id = fields[i].Trim('\n');
            if (!GitClient.IsCommitId(id))
            {
                break;
            }

            entries.Add(new StashEntry(entries.Count, id, fields[i + 2], GitClient.Date(fields[i + 1])));
        }

        return entries;
    }
}
