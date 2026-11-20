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
}
