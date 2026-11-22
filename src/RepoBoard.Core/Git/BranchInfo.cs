namespace RepoBoard.Core.Git;

/// <summary>A local branch, or a remote branch without a local one.</summary>
public sealed record BranchInfo(string Name, bool IsCurrent, bool IsRemote, string? Upstream, string? Track, string? LastCommit);
