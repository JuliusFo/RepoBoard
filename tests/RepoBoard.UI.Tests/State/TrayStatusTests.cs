using RepoBoard.Core.Board;
using RepoBoard.Core.Git;
using RepoBoard.Core.Repos;
using RepoBoard.UI.State;

namespace RepoBoard.UI.Tests.State;

/// <summary>The tooltip of the notification-area icon (ADR 0006).</summary>
public sealed class TrayStatusTests
{
    private static RepoState Repo(string name, int behind = 0, string? error = null) =>
        new(new RepoEntry(Path.Combine(Path.GetTempPath(), name)))
        {
            Status = new RepoStatus("main", "abc", "origin/main", 0, behind, []),
            Error = error,
        };

    [Fact]
    public void The_tooltip_counts_only_what_needs_a_look()
    {
        using var _ = UiCulture.Use("en");

        Assert.Equal("RepoBoard\n1 repository", TrayStatus.Text([Repo("a")], autoSyncPaused: false));
        Assert.Equal("RepoBoard\n3 repositories · 2 behind · 1 with problems\nAuto-sync paused",
            TrayStatus.Text([Repo("a", behind: 1), Repo("b", behind: 3), Repo("c", error: "failed")], autoSyncPaused: true));
    }

    /// <summary>Decision of the user (review WP-04 #11): the tooltip counts repositories, not their worktrees (review 0.4.0, C11).</summary>
    [Fact]
    public void Worktrees_do_not_count()
    {
        using var _ = UiCulture.Use("en");
        var repo = Repo("a");
        repo.Worktrees = [new RepoState(new RepoEntry(Path.Combine(Path.GetTempPath(), "a-wt")), repo)
        {
            Status = new RepoStatus("wt", "abc", "origin/wt", 0, 4, []),
        }];

        Assert.Equal("RepoBoard\n1 repository", TrayStatus.Text([repo], autoSyncPaused: false));
    }

    /// <summary>Windows takes 127 characters for the tooltip – .NET throws above that.</summary>
    [Fact]
    public void A_long_tooltip_is_cut_to_what_windows_takes()
    {
        var repos = Enumerable.Range(0, 2000).Select(i => Repo($"r{i}", behind: 1, error: "x")).ToList();

        var text = TrayStatus.Text(repos, autoSyncPaused: true);

        Assert.True(text.Length <= TrayStatus.Limit, $"{text.Length} characters");
    }
}
