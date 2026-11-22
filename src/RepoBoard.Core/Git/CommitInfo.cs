namespace RepoBoard.Core.Git;

/// <summary>A commit of the history (WP-09: with its parents, for the graph).</summary>
/// <param name="Id">The full commit id.</param>
/// <param name="ShortId">The abbreviated id git chose (unique in the repository).</param>
/// <param name="Parents">The full ids of its parents, the first parent first; none for a root commit.</param>
public sealed record CommitInfo(string Id, string ShortId, IReadOnlyList<string> Parents, string Author, string When, string Refs, string Subject)
{
    public bool IsMerge => Parents.Count > 1;
}
