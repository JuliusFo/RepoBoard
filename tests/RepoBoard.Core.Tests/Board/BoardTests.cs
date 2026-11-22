using Microsoft.Extensions.Time.Testing;
using RepoBoard.Core.Board;
using RepoBoard.Core.IO;
using RepoBoard.Core.Repos;

namespace RepoBoard.Core.Tests.Board;

/// <summary>The board's own operations against real repositories: commit, push, branches, scan.</summary>
public sealed class BoardTests : IDisposable
{
    private readonly GitSandbox _git = new();
    private readonly FakeTimeProvider _time = new();
    private readonly BoardService _board;

    public BoardTests() => _board = Boards.New(_git.Root, _time);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

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
        var found = await _board.ScanAsync(_git.Root, Ct); // mine and theirs; the bare remote has no worktree
        Assert.Equal(2, found.Count);
        Assert.Equal(2, await _board.AddScannedAsync(_git.Root, found, Ct));
        Assert.Empty(await _board.ScanAsync(_git.Root, Ct)); // what is on the board is not found again
        await _board.SetAutoPullAsync(_board.Repos.First(r => r.Name == "mine"), true, Ct);

        var reloaded = Boards.New(_git.Root, _time);
        await reloaded.LoadAsync(Ct);

        var mine = Assert.Single(reloaded.Repos, r => r.Name == "mine");
        Assert.True(mine.Entry is { AutoPull: true, AutoFetch: true });
    }

    /// <summary>
    /// Review 0.4.0, A5: "switch" on a remote branch ran <c>git switch &lt;name&gt;</c> – a click on upstream/main switched to the
    /// local main of origin, and a local branch of the same name won over the remote one.
    /// </summary>
    [Fact]
    public async Task Switching_to_a_remote_branch_tracks_exactly_that_one()
    {
        var other = Path.Combine(_git.Root, "other.git");
        GitSandbox.Git(_git.Root, "clone", "-q", "--bare", _git.Remote, other);
        GitSandbox.Git(other, "branch", "feat/x");
        GitSandbox.Git(_git.Mine, "remote", "add", "up/stream", other); // a remote name with "/" (allowed)
        GitSandbox.Git(_git.Mine, "fetch", "-q", "up/stream");
        var repo = await AddMineAsync(autoPull: false);
        await _board.SetWantsDetailsAsync(repo, true, Ct);

        var remote = Assert.Single(repo.Details!.Branches, b => b.Name == "up/stream/feat/x");
        Assert.True(await _board.SwitchAsync(repo, remote, Ct), repo.Error);

        Assert.Equal("feat/x", repo.Status!.Branch);
        Assert.Equal("up/stream/feat/x", repo.Status.Upstream);
        Assert.Contains(_board.Activity.Entries, e => e.Text.Contains("feat/x", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Switching_to_a_remote_branch_whose_local_name_is_taken_says_so()
    {
        GitSandbox.Git(_git.Theirs, "switch", "-q", "-c", "feature");
        GitSandbox.Commit(_git.Theirs, "f.txt", "theirs");
        GitSandbox.Git(_git.Theirs, "push", "-q", "origin", "feature");
        GitSandbox.Git(_git.Mine, "fetch", "-q");
        GitSandbox.Git(_git.Mine, "branch", "feature"); // a local "feature" of its own, without upstream
        var repo = await AddMineAsync(autoPull: false);
        await _board.SetWantsDetailsAsync(repo, true, Ct);

        var remote = Assert.Single(repo.Details!.Branches, b => b.Name == "origin/feature");
        Assert.False(await _board.SwitchAsync(repo, remote, Ct));

        Assert.Equal("main", repo.Status!.Branch);
        Assert.NotNull(repo.Error);
    }

    /// <summary>
    /// Review 0.4.0, A6: a repository folder whose .git is gone – git found the repository around it and the row showed,
    /// auto-synced and committed that one.
    /// </summary>
    [Fact]
    public async Task A_folder_that_lost_its_git_folder_never_works_on_the_repository_around_it()
    {
        GitSandbox.Git(_git.Root, "init", "-q", "-b", "main", "."); // the sandbox's folder: a repository around mine
        var repo = await AddMineAsync(autoPull: false);
        var gitDir = Path.Combine(_git.Mine, ".git");
        foreach (var file in Directory.EnumerateFiles(gitDir, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal); // git's objects are read-only
        }

        SafeDelete.DirectoryBelow(_git.Mine, gitDir);
        File.WriteAllText(Path.Combine(_git.Mine, "new.txt"), "work");

        await _board.RefreshAsync(repo, Ct);
        Assert.True(repo.Missing);
        Assert.Null(repo.Status);

        Assert.False(await _board.CommitAsync(repo, "must not land", push: false, Ct));
        Assert.False(GitSandbox.TryGit(_git.Root, "rev-parse", "--verify", "HEAD")); // still no commit around it
    }

    [Fact]
    public async Task Removing_a_repository_from_the_board_is_logged_and_keeps_the_folder()
    {
        var repo = await AddMineAsync(autoPull: false);

        await _board.RemoveAsync(repo, Ct);

        Assert.Empty(_board.Repos);
        Assert.True(Directory.Exists(_git.Mine));
        Assert.Contains(_board.Activity.Entries, e => e is { Kind: ActivityKind.Info } && e.Text.Contains(_git.Mine, StringComparison.Ordinal));
    }

    /// <summary>Review 0.4.0, A22: git's own message when it refuses a repository it found, not "is not a repository".</summary>
    [Fact]
    public async Task A_repository_git_refuses_is_reported_with_git_s_reason()
    {
        File.AppendAllText(Path.Combine(_git.Mine, ".git", "config"), "\n[broken\n");

        Assert.Equal(0, await _board.AddAsync([_git.Mine], Ct));

        var entry = Assert.Single(_board.Activity.Entries, e => e.Kind == ActivityKind.Error);
        Assert.Contains("config", entry.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("kein Git-Repository", entry.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Review 0.4.0, A23: a path written by hand with "/" or ".." stayed as written, and a scan put the same repository on
    /// the board a second time – two gates, two auto-fetches at once.
    /// </summary>
    [Fact]
    public async Task A_repository_written_another_way_in_the_config_is_not_added_twice()
    {
        var written = Path.Combine(_git.Mine, "sub", "..").Replace('\\', '/') + "/";
        await new ConfigStore(Path.Combine(_git.Root, "config.json"), _time).SaveAsync(new BoardConfig { Repos = [new RepoEntry(written)] }, Ct);
        await _board.LoadAsync(Ct);

        Assert.Equal(0, await _board.AddAsync([_git.Mine], Ct));

        Assert.Equal(_git.Mine, Assert.Single(_board.Repos).Path);
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
