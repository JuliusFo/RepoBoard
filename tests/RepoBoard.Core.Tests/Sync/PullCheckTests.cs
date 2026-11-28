using RepoBoard.Core.Git;
using RepoBoard.Core.Sync;

namespace RepoBoard.Core.Tests.Sync;

/// <summary>When auto-pull keeps out (ADR 0003). The cases with real repositories: <see cref="AutoSyncTests"/>.</summary>
public sealed class PullCheckTests
{
    [Theory]
    [InlineData("main", "origin/main", 0, 2, 0, null, null)] // only behind: fast-forward
    [InlineData(null, "origin/main", 0, 2, 0, null, "kein Branch ausgecheckt (detached HEAD)")]
    [InlineData("main", null, 0, 0, 0, null, "der Branch hat keinen Upstream")]
    [InlineData("main", "origin/main", 0, 2, 0, GitOperation.Rebase, "ein Rebase läuft noch")]
    [InlineData("main", "origin/main", 0, 2, 0, GitOperation.Merge, "ein Merge läuft noch")]
    [InlineData("main", "origin/main", 0, 2, 0, GitOperation.CherryPick, "ein Cherry-Pick läuft noch")]
    [InlineData("main", "origin/main", 0, 2, 0, GitOperation.Revert, "ein Revert läuft noch")] // git fast-forwards past REVERT_HEAD itself (review 0.4.0, C1)
    [InlineData("main", "origin/main", 0, 2, 1, null, "ungelöste Konflikte")]
    [InlineData("main", "origin/main", 1, 2, 0, null, "der Branch ist auseinandergelaufen (1 lokal, 2 remote) – bitte selbst mergen oder rebasen")]
    public void Only_a_branch_that_has_only_fallen_behind_is_fast_forwarded(
        string? branch, string? upstream, int ahead, int behind, int conflicts, GitOperation? running, string? expected)
    {
        var changes = Enumerable.Range(0, conflicts).Select(i => new FileChange($"c{i}.txt", null, 'U', 'U', false, true)).ToList();
        var status = new RepoStatus(branch, "abc", upstream, ahead, behind, changes);

        Assert.Equal(expected, PullCheck.Blocker(status, running));
    }
}
