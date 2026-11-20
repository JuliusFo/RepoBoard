using RepoBoard.Core.Git;

namespace RepoBoard.Core.Tests.Git;

public sealed class GitOutputTests
{
    [Fact]
    public void Status_reads_branch_upstream_and_changes()
    {
        var output = string.Join('\0',
            "# branch.oid 1234567890abcdef1234567890abcdef12345678",
            "# branch.head feature/x",
            "# branch.upstream origin/feature/x",
            "# branch.ab +2 -3",
            "1 M. N... 100644 100644 100644 aaaa bbbb src/a b.cs",
            "1 .M N... 100644 100644 100644 aaaa bbbb src/c.cs",
            "2 R. N... 100644 100644 100644 aaaa bbbb R100 new.cs",
            "old.cs",
            "u UU N... 100644 100644 100644 100644 aaaa bbbb cccc conflict.cs",
            "? neu ä.txt",
            "");

        var status = RepoStatus.Parse(output);

        Assert.Equal("feature/x", status.Branch);
        Assert.Equal("origin/feature/x", status.Upstream);
        Assert.Equal((2, 3), (status.Ahead, status.Behind));
        Assert.Equal(5, status.Changes.Count);
        Assert.Equal("src/a b.cs", status.Changes[0].Path);
        Assert.Equal(("new.cs", "old.cs"), (status.Changes[2].Path, status.Changes[2].OriginalPath));
        Assert.Equal("neu ä.txt", status.Changes[4].Path);
        Assert.Equal((2, 1, 1, 1), (status.Staged, status.Modified, status.Untracked, status.Conflicts));
    }

    [Fact]
    public void Status_of_a_new_repo_and_a_detached_head()
    {
        var initial = RepoStatus.Parse("# branch.oid (initial)\0# branch.head main\0");
        Assert.True(initial.IsInitial);
        Assert.False(initial.HasUpstream);

        var detached = RepoStatus.Parse("# branch.oid abc\0# branch.head (detached)\0");
        Assert.True(detached.IsDetached);
    }

    [Fact]
    public void Branches_list_locals_first_and_only_untracked_remotes()
    {
        var output = string.Join('\n',
            "refs/heads/main\torigin/main\t\t2 days ago",
            "refs/heads/feature\torigin/feature\tahead 1\t3 hours ago",
            "refs/remotes/origin/HEAD\t\t\t2 days ago",
            "refs/remotes/origin/main\t\t\t2 days ago",
            "refs/remotes/origin/feature\t\t\t3 hours ago",
            "refs/remotes/origin/other\t\t\t1 week ago");

        var branches = GitClient.ParseBranches(output, "feature");

        Assert.Equal(["feature", "main", "origin/other"], branches.Select(b => b.Name));
        Assert.True(branches[0].IsCurrent);
        Assert.Equal("ahead 1", branches[0].Track);
        Assert.True(branches[2].IsRemote);
    }

    [Fact]
    public void Log_splits_records_and_fields()
    {
        var log = GitClient.ParseLog("abc1234\u001fJo\u001f2 hours ago\u001fHEAD -> main, origin/main\u001fFix it\u001e\ndef5678\u001fJo\u001f3 days ago\u001f\u001fStart\u001e\n");

        Assert.Equal(2, log.Count);
        Assert.Equal("Fix it", log[0].Subject);
        Assert.Equal("", log[1].Refs);
    }
}
