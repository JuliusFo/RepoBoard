namespace RepoBoard.Core.Git;

/// <summary>A local branch, or a remote branch without a local one.</summary>
/// <param name="Ahead">Commits of the branch not on its upstream (0 without one).</param>
/// <param name="Behind">Commits of the upstream not on the branch.</param>
/// <param name="Gone">The branch names an upstream the remote no longer has (deleted after its pull request).</param>
/// <param name="LastCommit">When its last commit was made; null if git did not say.</param>
public sealed record BranchInfo(string Name, bool IsCurrent, bool IsRemote, string? Upstream, int Ahead, int Behind, bool Gone,
    DateTimeOffset? LastCommit);
