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

        // E2E WP-04: an upstream without "branch.ab" was deleted on the remote – not "up to date".
        var gone = RepoStatus.Parse("# branch.oid abc\0# branch.head f\0# branch.upstream origin/f\0");
        Assert.True(gone is { HasUpstream: true, UpstreamGone: true });
        Assert.False(RepoStatus.Parse("# branch.oid abc\0# branch.head f\0# branch.upstream origin/f\0# branch.ab +0 -0\0").UpstreamGone);
    }

    [Fact]
    public void Branches_list_locals_first_and_only_untracked_remotes()
    {
        var output = string.Join('\n',
            "refs/heads/main\torigin/main\t\t1795368460",
            "refs/heads/feature\torigin/feature\tahead 1\t1795368400",
            "refs/heads/both\torigin/both\tahead 2, behind 3\t1795368400",
            "refs/heads/merged\torigin/merged\tgone\t1795368400",
            "refs/remotes/origin/HEAD\t\t\t1795368460",
            "refs/remotes/origin/main\t\t\t1795368460",
            "refs/remotes/origin/feature\t\t\t1795368400",
            "refs/remotes/origin/other\t\t\t1795000000");

        var branches = GitClient.ParseBranches(output, "feature");

        Assert.Equal(["feature", "both", "main", "merged", "origin/other"], branches.Select(b => b.Name));
        Assert.True(branches[0].IsCurrent);
        Assert.Equal((1, 0, false), (branches[0].Ahead, branches[0].Behind, branches[0].Gone));
        Assert.Equal((2, 3, false), (branches[1].Ahead, branches[1].Behind, branches[1].Gone));
        Assert.Equal((0, 0, false), (branches[2].Ahead, branches[2].Behind, branches[2].Gone));
        Assert.True(branches[3].Gone);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795368460), branches[2].LastCommit);
        Assert.True(branches[4].IsRemote);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795000000), branches[4].LastCommit);
    }

    private const string A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string C = "cccccccccccccccccccccccccccccccccccccccc";

    [Fact]
    public void Log_reads_seven_fields_per_commit()
    {
        var log = GitClient.ParseLog(
            $"{A}\0aaaaaaa\0{B} {C}\0Jo\01795368460\0HEAD -> main, origin/main\0Fix it\0" +
            $"{B}\0bbbbbbb\0\0Jo\01795000000\0\0Start\0");

        Assert.Equal(2, log.Count);
        Assert.Equal((A, "aaaaaaa", "Fix it"), (log[0].Id, log[0].ShortId, log[0].Subject));
        Assert.Equal([B, C], log[0].Parents);
        Assert.True(log[0].IsMerge);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795368460), log[0].When);
        Assert.Empty(log[1].Parents);
        Assert.Equal("", log[1].Refs);
    }

    /// <summary>Review 0.4.0, A7: a stranger's commit must not add an entry of its own or shift the fields of its row.</summary>
    [Fact]
    public void Log_stops_at_output_that_is_out_of_step()
    {
        var log = GitClient.ParseLog($"{A}\0aaaaaaa\0\0Jo\01795368460\0\0Fix it\0not-a-commit\0x\0\0Jo\01\0\0more\0");

        Assert.Equal(A, Assert.Single(log).Id);
    }

    [Theory]
    [InlineData("origin/feature", "feature")]
    [InlineData("origin/feat/x", "feat/x")]
    [InlineData("up/stream/feat/x", "feat/x")] // the longest remote wins
    [InlineData("up/x", "x")]
    [InlineData("unknown/x", null)]
    [InlineData("origin/", null)]
    public void A_remote_branch_s_local_name_follows_its_remote(string remoteBranch, string? local) =>
        Assert.Equal(local, GitClient.LocalNameOf(remoteBranch, ["origin", "up", "up/stream"]));

    [Theory]
    [InlineData("Fix it", "Fix it", "")]
    [InlineData("Fix crash when\nthe repo is gone\n\nDetails\nmore", "Fix crash when the repo is gone", "Details\nmore")]
    [InlineData("\n\nSubject\r\n\r\nBody\r\n", "Subject", "Body")]
    [InlineData("Subject\n\n\nBody\n\nSecond paragraph\n", "Subject", "Body\n\nSecond paragraph")]
    public void A_message_splits_into_subject_and_body_as_git_does(string message, string subject, string body) =>
        Assert.Equal((subject, body), (CommitMessage.Subject(message), CommitMessage.Body(message)));
}
