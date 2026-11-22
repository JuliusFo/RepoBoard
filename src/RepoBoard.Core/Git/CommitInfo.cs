namespace RepoBoard.Core.Git;

/// <summary>A commit of the history (WP-09: with its parents, for the graph).</summary>
/// <param name="Id">The full commit id.</param>
/// <param name="ShortId">The abbreviated id git chose (unique in the repository).</param>
/// <param name="Parents">The full ids of its parents, the first parent first; none for a root commit.</param>
/// <param name="When">The commit's date (committer); shown relative to now in the UI's language (review 0.4.0, A19).</param>
/// <param name="Subject">The first paragraph of the message, its lines joined (git's <c>%s</c>, <see cref="CommitMessage.Subject"/>).</param>
public sealed record CommitInfo(string Id, string ShortId, IReadOnlyList<string> Parents, string Author, DateTimeOffset When, string Refs, string Subject)
{
    public bool IsMerge => Parents.Count > 1;
}
