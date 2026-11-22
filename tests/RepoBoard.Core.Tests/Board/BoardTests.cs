using Microsoft.Extensions.Time.Testing;
using RepoBoard.Core.Board;

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
        Assert.Equal(2, await _board.ScanAsync(_git.Root, Ct)); // mine and theirs; the bare remote has no worktree
        await _board.SetAutoPullAsync(_board.Repos.First(r => r.Name == "mine"), true, Ct);

        var reloaded = Boards.New(_git.Root, _time);
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
