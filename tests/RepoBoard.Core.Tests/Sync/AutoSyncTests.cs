using Microsoft.Extensions.Time.Testing;
using RepoBoard.Core.Board;
using RepoBoard.Core.Git;
using RepoBoard.Core.Repos;
using RepoBoard.Core.Sync;

namespace RepoBoard.Core.Tests.Sync;

/// <summary>Auto-fetch and auto-pull against real repositories (ADR 0003): what they do, and what they leave alone.</summary>
public sealed class AutoSyncTests : IDisposable
{
    private readonly GitSandbox _git = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-11-20T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly BoardService _board;

    public AutoSyncTests() => _board = NewBoard();

    private BoardService NewBoard() => Boards.New(_git.Root, _time);

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

    /// <summary>
    /// Review 0.4.0, A1: with the user's autostash git stashes the overlapping change, fast-forwards and puts it back with
    /// conflict markers – and ends with success. Auto-pull and a click on Pull must refuse instead, as without it.
    /// </summary>
    [Theory]
    [InlineData(true, "merge.autoStash", "true")]
    [InlineData(false, "pull.rebase", "true", "rebase.autoStash", "true")]
    [InlineData(false, "merge.autoStash", "true")]
    public async Task The_users_autostash_never_puts_local_changes_back_with_conflicts(bool auto, params string[] config)
    {
        var repo = await AddMineAsync(autoPull: auto);
        for (var i = 0; i < config.Length; i += 2)
        {
            GitSandbox.Git(_git.Mine, "config", config[i], config[i + 1]);
        }

        GitSandbox.Commit(_git.Theirs, "a.txt", "theirs");
        GitSandbox.Git(_git.Theirs, "push");
        File.WriteAllText(Path.Combine(_git.Mine, "a.txt"), "my unsaved work");

        if (auto)
        {
            await _board.AutoSyncAsync(repo, Ct);
        }
        else
        {
            Assert.False(await _board.PullAsync(repo, Ct));
        }

        Assert.NotNull(repo.Error);
        Assert.Equal("my unsaved work", File.ReadAllText(Path.Combine(_git.Mine, "a.txt")));
        Assert.Equal("", GitSandbox.Git(_git.Mine, "stash", "list"));
        Assert.Equal(0, repo.Status!.Conflicts);
    }

    /// <summary>
    /// Review 0.4.0, A2: the fetch before succeeds and cleared the row's error, so the same failed fast-forward was logged –
    /// and notified – on every interval until the user committed.
    /// </summary>
    [Fact]
    public async Task A_fast_forward_local_changes_keep_out_is_logged_once()
    {
        var repo = await AddMineAsync(autoPull: true);
        GitSandbox.Commit(_git.Theirs, "a.txt", "theirs");
        GitSandbox.Git(_git.Theirs, "push");
        File.WriteAllText(Path.Combine(_git.Mine, "a.txt"), "my unsaved work");

        await _board.AutoSyncAsync(repo, Ct);
        await _board.AutoSyncAsync(repo, Ct);

        Assert.NotNull(repo.Error);
        Assert.Single(_board.Activity.Entries, e => e.Kind == ActivityKind.Error);
    }

    /// <summary>Offline or signed out, every interval fails the same way: one entry, not one per interval (ADR 0003, review 0.4.0, C1).</summary>
    [Fact]
    public async Task The_same_failed_fetch_is_logged_once()
    {
        var repo = await AddMineAsync(autoPull: false);
        await _board.SetAutoFetchAsync(repo, true, Ct);
        GitSandbox.Git(_git.Mine, "remote", "set-url", "origin", Path.Combine(_git.Root, "gone.git"));

        await _board.AutoSyncAsync(repo, Ct);
        await _board.AutoSyncAsync(repo, Ct);

        Assert.NotNull(repo.Error);
        Assert.Single(_board.Activity.Entries, e => e.Kind == ActivityKind.Error);
    }

    /// <summary>
    /// A stopped revert: git itself fast-forwards past REVERT_HEAD, so <see cref="PullCheck"/> is the only thing that keeps
    /// auto-pull out (ADR 0003, review 0.4.0, C1 – reproduced with git 2.41).
    /// </summary>
    [Fact]
    public async Task Auto_pull_keeps_out_of_an_unfinished_revert()
    {
        var repo = await AddMineAsync(autoPull: true);
        File.WriteAllText(Path.Combine(_git.Mine, ".git", "REVERT_HEAD"), GitSandbox.Git(_git.Mine, "rev-parse", "HEAD"));
        GitSandbox.Commit(_git.Theirs, "b.txt", "two");
        GitSandbox.Git(_git.Theirs, "push");

        await _board.AutoSyncAsync(repo, Ct);

        Assert.Equal(1, repo.Status!.Behind);
        Assert.NotNull(repo.AutoPullBlocked);
        Assert.False(File.Exists(Path.Combine(_git.Mine, "b.txt")));
    }

    [Fact]
    public async Task A_busy_repository_is_left_for_the_next_tick()
    {
        var repo = await AddMineAsync(autoPull: true);
        await repo.Gate.WaitAsync(Ct);
        try
        {
            await _board.AutoSyncAsync(repo, Ct);
        }
        finally
        {
            repo.Gate.Release();
        }

        Assert.Null(repo.LastAutoSync);
    }

    /// <summary>
    /// Review 0.4.0, A18: a clone of an empty repository has no commit, git writes no ahead/behind for it – it read as
    /// "upstream deleted" and never got the first commit a colleague pushed.
    /// </summary>
    [Fact]
    public async Task A_clone_of_an_empty_repository_gets_the_first_commit()
    {
        var remote = Path.Combine(_git.Root, "empty.git");
        var mine = Path.Combine(_git.Root, "empty-mine");
        var theirs = Path.Combine(_git.Root, "empty-theirs");
        GitSandbox.Git(_git.Root, "init", "-q", "--bare", "-b", "main", remote);
        GitSandbox.Git(_git.Root, "clone", "-q", remote, mine);
        GitSandbox.Git(_git.Root, "clone", "-q", remote, theirs);
        await _board.AddAsync([mine], Ct);
        var repo = Assert.Single(_board.Repos);
        await _board.SetAutoPullAsync(repo, true, Ct);
        Assert.False(repo.Status!.UpstreamGone);

        GitSandbox.Commit(theirs, "first.txt", "hello");
        GitSandbox.Git(theirs, "push", "-q", "origin", "main");
        await _board.AutoSyncAsync(repo, Ct);

        Assert.Null(repo.Error);
        Assert.True(File.Exists(Path.Combine(mine, "first.txt")));
        Assert.False(repo.Status!.IsInitial);
    }

    /// <summary>The fast-forward a click on Pull makes with the user's <c>pull.rebase</c>: unrelated local changes stay (review 0.4.0, A1).</summary>
    [Fact]
    public async Task Pull_fast_forwards_past_unrelated_local_changes_whatever_pull_rebase_says()
    {
        var repo = await AddMineAsync(autoPull: false);
        GitSandbox.Git(_git.Mine, "config", "pull.rebase", "true");
        GitSandbox.Commit(_git.Theirs, "b.txt", "two");
        GitSandbox.Git(_git.Theirs, "push");
        File.WriteAllText(Path.Combine(_git.Mine, "a.txt"), "my unsaved work");

        Assert.True(await _board.PullAsync(repo, Ct), repo.Error);

        Assert.True(File.Exists(Path.Combine(_git.Mine, "b.txt")));
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

    /// <summary>Review WP-02: real fetches take seconds; a run that outlasts the gathering time is still one notification.</summary>
    [Fact]
    public async Task One_auto_sync_run_is_one_notification_however_long_its_fetches_take()
    {
        var mine = await AddMineAsync(autoPull: true);
        var second = Path.Combine(_git.Root, "second");
        GitSandbox.Git(_git.Root, "clone", "-q", _git.Remote, second);
        await _board.AddAsync([second], Ct);
        await _board.SetAutoPullAsync(_board.Repos.Single(r => r != mine), true, Ct);
        GitSandbox.Commit(_git.Theirs, "b.txt", "two");
        GitSandbox.Git(_git.Theirs, "push");
        using var notifier = new SyncNotifier(_board, _time);
        var shown = new List<SyncNotification>();
        notifier.Show += shown.Add;
        _board.Activity.Added += _ => _time.Advance(SyncNotifier.Gather + TimeSpan.FromSeconds(1)); // each pull "takes" 3 s

        await _board.AutoSyncDueAsync(Ct);

        var notification = Assert.Single(shown);
        Assert.Contains("2", notification.Title, StringComparison.Ordinal);
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
