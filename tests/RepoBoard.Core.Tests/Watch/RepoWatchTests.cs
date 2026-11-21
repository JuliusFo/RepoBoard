using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RepoBoard.Core.Board;
using RepoBoard.Core.Git;
using RepoBoard.Core.Repos;
using RepoBoard.Core.Watch;

namespace RepoBoard.Core.Tests.Watch;

/// <summary>
/// File watchers on real repositories (WP-03, ADR 0011): what git does outside RepoBoard shows without a refresh, and the
/// tick reads watched repositories only as a fallback. Real time: the events come from the file system.
/// </summary>
public sealed class RepoWatchTests : IDisposable
{
    private readonly GitSandbox _git = new();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private BoardService NewBoard(TimeProvider time) =>
        new(new GitClient(new GitRunner()), new ConfigStore(Path.Combine(_git.Root, "config.json"), time), time);

    private async Task<(BoardService Board, RepoState Repo, RepoWatchService Watch)> WatchMineAsync()
    {
        var board = NewBoard(TimeProvider.System);
        await board.AddAsync([_git.Mine], Ct);
        var repo = board.Repos.Single();
        var watch = new RepoWatchService(board, TimeProvider.System, NullLogger<RepoWatchService>.Instance);
        await watch.StartAsync(Ct);
        await Wait.UntilAsync(() => repo.Watched, "the watcher on the repository");
        return (board, repo, watch);
    }

    public static TheoryData<string> OutsideChanges => ["commit", "switch", "new file", "stage", "fetch"];

    [Theory]
    [MemberData(nameof(OutsideChanges))]
    public async Task What_git_does_outside_shows_without_a_refresh(string change)
    {
        var (_, repo, watch) = await WatchMineAsync();
        try
        {
            Func<RepoStatus, bool> shows;
            switch (change)
            {
                case "commit":
                    GitSandbox.Commit(_git.Mine, "b.txt", "two");
                    shows = s => s.Ahead == 1;
                    break;
                case "switch":
                    GitSandbox.Git(_git.Mine, "switch", "-c", "feature");
                    shows = s => s.Branch == "feature";
                    break;
                case "new file":
                    File.WriteAllText(Path.Combine(_git.Mine, "new.txt"), "new");
                    shows = s => s.Untracked == 1;
                    break;
                case "stage":
                    File.WriteAllText(Path.Combine(_git.Mine, "a.txt"), "changed");
                    GitSandbox.Git(_git.Mine, "add", "a.txt");
                    shows = s => s.Staged == 1;
                    break;
                default:
                    GitSandbox.Commit(_git.Theirs, "b.txt", "theirs");
                    GitSandbox.Git(_git.Theirs, "push");
                    GitSandbox.Git(_git.Mine, "fetch");
                    shows = s => s.Behind == 1;
                    break;
            }

            await Wait.UntilAsync(() => repo.Status is { } s && shows(s), $"the {change} on the board");
        }
        finally
        {
            await watch.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task A_worktree_added_outside_shows_with_its_status_and_its_commits()
    {
        var (_, repo, watch) = await WatchMineAsync();
        try
        {
            var path = Path.Combine(_git.Mine, ".claude", "worktrees", "session");
            GitSandbox.Git(_git.Mine, "worktree", "add", "-b", "session", path);

            await Wait.UntilAsync(() => repo.Worktrees is [{ Status: not null }], "the new worktree with its status");
            var worktree = repo.Worktrees.Single();
            GitSandbox.Commit(path, "w.txt", "in the worktree");
            var head = GitSandbox.Git(path, "rev-parse", "HEAD").Trim();

            await Wait.UntilAsync(() => worktree.Status?.Head == head, "the worktree's commit");
            Assert.True(worktree.Watched); // inside the repository: its watcher sees it
        }
        finally
        {
            await watch.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task The_watchers_follow_the_board()
    {
        var (board, repo, watch) = await WatchMineAsync();
        try
        {
            var beside = Path.Combine(_git.Root, "beside");
            GitSandbox.Git(_git.Mine, "worktree", "add", "-b", "beside", beside);
            await board.RefreshAsync(repo, Ct);

            await Wait.UntilAsync(() => watch.WatchedRoots.Count == 2 && repo.Worktrees is [{ Watched: true }], "a watcher on the worktree beside");

            // The watcher's handle on the folder does not keep git from removing it (Windows).
            GitSandbox.Git(_git.Mine, "worktree", "remove", beside);
            Assert.False(Directory.Exists(beside));
            await Wait.UntilAsync(() => repo.Worktrees.IsEmpty && watch.WatchedRoots.Count == 1, "the worktree and its watcher gone");

            await board.RemoveAsync(repo, Ct);

            await Wait.UntilAsync(() => watch.WatchedRoots.Count == 0, "no watcher once the repository is off the board");
        }
        finally
        {
            await watch.StopAsync(Ct);
        }
    }

    /// <summary>
    /// A read must not cause the next – also where RepoBoard's own <c>git status</c> writes into the git folder: fsmonitor
    /// leaves a cookie there on every status (review WP-03, reproduced). Waiting a fixed time is the point here – nothing
    /// may happen in it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reading_the_status_does_not_read_it_again(bool fsmonitor)
    {
        if (fsmonitor)
        {
            GitSandbox.Git(_git.Mine, "config", "core.fsmonitor", "true");
            Assert.SkipUnless(GitSandbox.TryGit(_git.Mine, "fsmonitor--daemon", "start"), "git has no fsmonitor daemon on this platform.");
        }

        try
        {
            var (_, repo, watch) = await WatchMineAsync();
            try
            {
                File.WriteAllText(Path.Combine(_git.Mine, "new.txt"), "new");
                await Wait.UntilAsync(() => repo.Status is { Untracked: 1 }, "the new file on the board");
                var read = repo.StatusReadAt;

                await Task.Delay(RepoWatchService.Longest, Ct);

                Assert.Equal(read, repo.StatusReadAt);
            }
            finally
            {
                await watch.StopAsync(Ct);
            }
        }
        finally
        {
            if (fsmonitor)
            {
                GitSandbox.TryGit(_git.Mine, "fsmonitor--daemon", "stop"); // it holds the folder the test deletes
            }
        }
    }

    /// <summary>An operation holds the gate when the change comes: the watcher reads once it is free (review WP-03).</summary>
    [Fact]
    public async Task A_change_while_the_repository_is_busy_is_read_afterwards()
    {
        var (_, repo, watch) = await WatchMineAsync();
        try
        {
            await repo.Gate.WaitAsync(Ct);
            try
            {
                File.WriteAllText(Path.Combine(_git.Mine, "new.txt"), "new");
                await Task.Delay(RepoWatchService.Longest, Ct); // the watcher's read came and was skipped
                Assert.Equal(0, repo.Status!.Untracked);
            }
            finally
            {
                repo.Gate.Release();
            }

            await Wait.UntilAsync(() => repo.Status is { Untracked: 1 }, "the new file once the gate is free");
        }
        finally
        {
            await watch.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task A_full_buffer_reads_everything_under_the_watcher_again()
    {
        var (_, repo, watch) = await WatchMineAsync();
        try
        {
            var read = repo.StatusReadAt;

            watch.OnError(watch.WatchedRoots.Single(), new InternalBufferOverflowException());

            await Wait.UntilAsync(() => repo.StatusReadAt > read, "a read after the overflow");
            Assert.True(repo.Watched);
        }
        finally
        {
            await watch.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task A_failed_watcher_leaves_the_repository_to_the_tick_and_is_tried_again()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-21T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var board = NewBoard(time);
        await board.AddAsync([_git.Mine], Ct);
        var repo = board.Repos.Single();
        var watch = new RepoWatchService(board, time, NullLogger<RepoWatchService>.Instance);
        await watch.StartAsync(Ct);
        try
        {
            await Wait.UntilAsync(() => repo.Watched, "the watcher on the repository");

            watch.OnError(watch.WatchedRoots.Single(), new IOException("The network name is no longer available."));
            await Wait.UntilAsync(() => !repo.Watched && watch.WatchedRoots.Count == 0, "the watcher stopped");

            // Fake time moves on while the test waits: the loop may compute its wait before or after a step.
            await Wait.UntilAsync(() =>
            {
                time.Advance(TimeSpan.FromSeconds(5));
                return repo.Watched;
            }, "the watcher back after a minute");
        }
        finally
        {
            await watch.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task The_tick_reads_a_watched_repository_only_as_a_fallback()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-21T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var board = NewBoard(time);
        await board.AddAsync([_git.Mine], Ct);
        var repo = board.Repos.Single();
        var added = repo.StatusReadAt;

        repo.Watched = true;
        time.Advance(BoardService.WatchedRefresh - TimeSpan.FromSeconds(1));
        await board.RefreshStaleAsync(Ct);
        Assert.Equal(added, repo.StatusReadAt);

        time.Advance(TimeSpan.FromSeconds(1));
        await board.RefreshStaleAsync(Ct);
        Assert.Equal(time.GetTimestamp(), repo.StatusReadAt);

        // A status that fails keeps the time of the last good one, and the tick reads it every time (review WP-03).
        var gitDir = Path.Combine(_git.Mine, ".git");
        Directory.Move(gitDir, gitDir + "-away");
        try
        {
            var good = repo.StatusReadAt;
            time.Advance(TimeSpan.FromSeconds(20));
            await board.RefreshAsync(repo, Ct);
            Assert.NotNull(repo.Error);
            Assert.Equal(good, repo.StatusReadAt);
        }
        finally
        {
            Directory.Move(gitDir + "-away", gitDir);
        }

        time.Advance(TimeSpan.FromSeconds(20));
        await board.RefreshStaleAsync(Ct);
        var again = repo.StatusReadAt;
        Assert.Equal(time.GetTimestamp(), again);

        // Read again, the error stays in the row – the tick goes back to the fallback (review WP-03).
        time.Advance(TimeSpan.FromSeconds(20));
        await board.RefreshStaleAsync(Ct);
        Assert.NotNull(repo.Error);
        Assert.Equal(again, repo.StatusReadAt);

        repo.Watched = false; // no watcher: every tick
        time.Advance(TimeSpan.FromSeconds(20));
        await board.RefreshStaleAsync(Ct);
        Assert.Equal(time.GetTimestamp(), repo.StatusReadAt);
    }

    public void Dispose() => _git.Dispose();
}
