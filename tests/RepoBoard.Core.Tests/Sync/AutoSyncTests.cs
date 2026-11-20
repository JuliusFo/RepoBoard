using Microsoft.Extensions.Time.Testing;
using RepoBoard.Core.Board;
using RepoBoard.Core.Git;
using RepoBoard.Core.Repos;

namespace RepoBoard.Core.Tests.Sync;

/// <summary>Auto-fetch and auto-pull against real repositories (ADR 0003): what they do, and what they leave alone.</summary>
public sealed class AutoSyncTests : IDisposable
{
    private readonly GitSandbox _git = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-11-20T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly BoardService _board;

    public AutoSyncTests() => _board = NewBoard();

    private BoardService NewBoard() =>
        new(new GitClient(new GitRunner()), new ConfigStore(Path.Combine(_git.Root, "config.json"), _time), _time);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Auto_pull_fast_forwards_new_commits()
    {
        var repo = await AddMineAsync(autoPull: true);
        GitSandbox.Commit(_git.Theirs, "b.txt", "two");
        GitSandbox.Commit(_git.Theirs, "c.txt", "three");
        GitSandbox.Git(_git.Theirs, "push");

        await _board.AutoSyncAsync(repo, Ct);

        Assert.Null(repo.Error);
        Assert.Equal(0, repo.Status!.Behind);
        Assert.True(File.Exists(Path.Combine(_git.Mine, "c.txt")));
        Assert.Contains(_board.Activity.Entries, e => e is { Text: "2 Commits gepullt (main)", Kind: ActivityKind.Pulled, IsAuto: true } && e.RepoPath == repo.Path);
    }

    [Fact]
    public async Task Auto_fetch_alone_only_reports_how_far_behind()
    {
        var repo = await AddMineAsync(autoPull: false);
        await _board.SetAutoFetchAsync(repo, true, Ct);
        GitSandbox.Commit(_git.Theirs, "b.txt", "two");
        GitSandbox.Git(_git.Theirs, "push");

        await _board.AutoSyncAsync(repo, Ct);

        Assert.Equal(1, repo.Status!.Behind);
        Assert.False(File.Exists(Path.Combine(_git.Mine, "b.txt")));
        Assert.Contains(_board.Activity.Entries, e => e is { Text: "1 Commit neu auf origin/main", Kind: ActivityKind.NewCommits });
    }

    [Fact]
    public async Task Auto_pull_leaves_a_diverged_branch_alone_and_says_so_once()
    {
        var repo = await AddMineAsync(autoPull: true);
        GitSandbox.Commit(_git.Theirs, "b.txt", "theirs");
        GitSandbox.Git(_git.Theirs, "push");
        GitSandbox.Commit(_git.Mine, "local.txt", "mine");

        await _board.AutoSyncAsync(repo, Ct);
        await _board.AutoSyncAsync(repo, Ct);

        Assert.Equal((1, 1), (repo.Status!.Ahead, repo.Status.Behind));
        Assert.NotNull(repo.AutoPullBlocked);
        Assert.False(File.Exists(Path.Combine(_git.Mine, "b.txt")));
        Assert.Single(_board.Activity.Entries, e => e.Text.StartsWith("Auto-Pull übersprungen", StringComparison.Ordinal) && e.Kind == ActivityKind.Skipped);
    }

    [Fact]
    public async Task Auto_pull_keeps_local_changes_it_would_overwrite()
    {
        var repo = await AddMineAsync(autoPull: true);
        GitSandbox.Commit(_git.Theirs, "a.txt", "theirs");
        GitSandbox.Git(_git.Theirs, "push");
        File.WriteAllText(Path.Combine(_git.Mine, "a.txt"), "my unsaved work");

        await _board.AutoSyncAsync(repo, Ct);

        Assert.NotNull(repo.Error);
        Assert.Equal("my unsaved work", File.ReadAllText(Path.Combine(_git.Mine, "a.txt")));
    }

    [Fact]
    public async Task A_repository_is_due_again_only_after_the_interval()
    {
        var repo = await AddMineAsync(autoPull: false);
        await _board.SetAutoFetchAsync(repo, true, Ct);
        await _board.AutoSyncDueAsync(Ct);
        var first = repo.LastAutoSync;

        _time.Advance(TimeSpan.FromMinutes(_board.AutoSyncMinutes) - TimeSpan.FromSeconds(1));
        await _board.AutoSyncDueAsync(Ct);
        Assert.Equal(first, repo.LastAutoSync);

        _time.Advance(TimeSpan.FromSeconds(1));
        await _board.AutoSyncDueAsync(Ct);
        Assert.Equal(_time.GetUtcNow(), repo.LastAutoSync);
    }

    [Fact]
    public async Task A_paused_auto_sync_syncs_nothing_until_resumed()
    {
        var repo = await AddMineAsync(autoPull: true);
        GitSandbox.Commit(_git.Theirs, "b.txt", "two");
        GitSandbox.Git(_git.Theirs, "push");

        _board.SetAutoSyncPaused(true);
        await _board.AutoSyncDueAsync(Ct);
        Assert.Null(repo.LastAutoSync);
        Assert.False(File.Exists(Path.Combine(_git.Mine, "b.txt")));

        _board.SetAutoSyncPaused(false);
        await _board.AutoSyncDueAsync(Ct);
        Assert.True(File.Exists(Path.Combine(_git.Mine, "b.txt")));
    }

    [Fact]
    public async Task Commit_all_and_push_from_the_board()
    {
        var repo = await AddMineAsync(autoPull: false);
        File.WriteAllText(Path.Combine(_git.Mine, "new.txt"), "hello");
        await _board.RefreshAsync(repo, Ct);

        Assert.True(await _board.CommitAsync(repo, "Add new.txt", push: true, Ct), repo.Error);

        Assert.True(repo.Status!.IsClean);
        Assert.Equal(0, repo.Status.Ahead);
        GitSandbox.Git(_git.Theirs, "pull");
        Assert.True(File.Exists(Path.Combine(_git.Theirs, "new.txt")));
    }

    [Theory]
    [InlineData("-d")] // would be an option of git switch
    [InlineData("--orphan")]
    [InlineData("bad..name")]
    [InlineData("with space")]
    public async Task A_branch_name_git_does_not_accept_never_reaches_git_switch(string name)
    {
        var repo = await AddMineAsync(autoPull: false);

        Assert.False(await _board.CreateBranchAsync(repo, name, Ct));

        Assert.Contains(name, repo.Error, StringComparison.Ordinal);
        Assert.Equal("main", repo.Status!.Branch);
    }

    [Fact]
    public async Task Scan_finds_the_repositories_and_the_config_keeps_the_switches()
    {
        Assert.Equal(2, await _board.ScanAsync(_git.Root, Ct)); // mine and theirs; the bare remote has no worktree
        await _board.SetAutoPullAsync(_board.Repos.First(r => r.Name == "mine"), true, Ct);

        var reloaded = NewBoard();
        await reloaded.LoadAsync(Ct);

        var mine = Assert.Single(reloaded.Repos, r => r.Name == "mine");
        Assert.True(mine.Entry is { AutoPull: true, AutoFetch: true });
    }

    private async Task<RepoState> AddMineAsync(bool autoPull)
    {
        await _board.AddAsync([_git.Mine], Ct);
        var repo = Assert.Single(_board.Repos);
        if (autoPull)
        {
            await _board.SetAutoPullAsync(repo, true, Ct);
        }

        return repo;
    }

    public void Dispose() => _git.Dispose();
}
