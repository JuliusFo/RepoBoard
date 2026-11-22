using RepoBoard.Core.Git;

namespace RepoBoard.Core.Board;

/// <summary>What the expanded row shows besides the status.</summary>
/// <param name="Graph">One row of the graph per commit of <paramref name="Log"/> (WP-09).</param>
/// <param name="HasMore">The history goes on beyond <paramref name="Log"/>.</param>
/// <param name="HeadOnRemote">HEAD is on a remote branch: amending it would need a force push. Null if git could not tell –
/// amend stays off then too, saying why (review 0.4.0, A38).</param>
public sealed record RepoDetails(IReadOnlyList<BranchInfo> Branches, IReadOnlyList<CommitInfo> Log, IReadOnlyList<GraphRow> Graph,
    bool HasMore, bool? HeadOnRemote);
