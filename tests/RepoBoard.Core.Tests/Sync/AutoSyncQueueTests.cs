using Microsoft.Extensions.Time.Testing;
using RepoBoard.Core.Board;
using RepoBoard.Core.Git;
using RepoBoard.Core.IO;
using RepoBoard.Core.Repos;

namespace RepoBoard.Core.Tests.Sync;

/// <summary>
/// How many repositories auto-sync and "Fetch all" work on at once, with a git that only counts (review 0.4.0, A3): a
/// repository waiting for the network held its gate meanwhile – spinner, commit locked, no status –, minutes offline.
/// </summary>
public sealed class AutoSyncQueueTests : IDisposable
{
    private readonly TestFolder _folder = new();
    private readonly FakeTimeProvider _time = new();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Auto_sync_holds_no_more_gates_than_it_fetches_and_leaves_a_slot_for_clicks()
    {
        var (board, git) = await BoardAsync(repos: 7);

        await board.AutoSyncDueAsync(Ct);

        Assert.Equal(7, git.Fetches);
        Assert.Equal(BoardService.MaxParallelNetwork - 1, git.MostAtOnce);
        Assert.True(git.MostBusy <= BoardService.MaxParallelNetwork - 1, $"{git.MostBusy} repositories were busy at once");
    }

    [Fact]
    public async Task Fetch_all_skips_missing_repositories_and_waits_in_line_without_a_gate()
    {
        var (board, git) = await BoardAsync(repos: 6);
        var missing = board.Repos[0];
        SafeDelete.DirectoryBelow(_folder.Path, missing.Path);
        await board.RefreshAsync(missing, Ct);

        await board.FetchAllAsync(Ct);

        Assert.Equal(5, git.Fetches);
        Assert.True(git.MostBusy <= BoardService.MaxParallelNetwork, $"{git.MostBusy} repositories were busy at once");
    }

    /// <summary>Review 0.4.0, A15 (reproduced): a fetch of the repository beside a pull in its worktree – "cannot lock ref".</summary>
    [Fact]
    public async Task A_repository_and_its_worktrees_reach_the_network_one_after_the_other()
    {
        var (board, git) = await BoardAsync(repos: 1);
        var repo = board.Repos[0];
        var worktreePath = _folder.Combine("worktree");
        Directory.CreateDirectory(worktreePath);
        File.WriteAllText(Path.Combine(worktreePath, ".git"), "gitdir: elsewhere");
        git.Worktrees = $"worktree {repo.Path}\0HEAD {new string('a', 40)}\0branch refs/heads/main\0\0"
            + $"worktree {worktreePath}\0HEAD {new string('b', 40)}\0branch refs/heads/side\0\0";
        await board.RefreshAsync(repo, Ct);
        var worktree = Assert.Single(repo.Worktrees);

        await Task.WhenAll(board.FetchAsync(repo, Ct), board.PullAsync(worktree, Ct), board.FetchAsync(worktree, Ct));

        Assert.Equal(3, git.Fetches);
        Assert.Equal(1, git.MostAtOnce);
    }

    /// <summary>
    /// Review 0.4.0, A9 (decision of the user): a worktree entry on another host's share comes from a file in the repository –
    /// reading its status every tick would send a sign-in there. Only for a repository on a share itself.
    /// </summary>
    [Fact]
    public async Task A_worktree_on_a_share_is_left_out_for_a_repository_that_is_not_on_one()
    {
        var (board, git) = await BoardAsync(repos: 1);
        var repo = board.Repos[0];
        git.Worktrees = $"worktree {repo.Path}\0HEAD {new string('a', 40)}\0branch refs/heads/main\0\0"
            + $"worktree \\\\host\\share\\wt\0HEAD {new string('b', 40)}\0branch refs/heads/side\0\0";

        await board.RefreshAsync(repo, Ct);

        Assert.Empty(repo.Worktrees);
    }

    private async Task<(BoardService Board, CountingGit Git)> BoardAsync(int repos)
    {
        var entries = Enumerable.Range(0, repos).Select(i =>
        {
            var path = _folder.Combine($"repo{i}");
            Directory.CreateDirectory(Path.Combine(path, ".git"));
            return new RepoEntry(path, AutoFetch: true);
        }).ToList();
        var store = new ConfigStore(_folder.Combine("config.json"), _time);
        await store.SaveAsync(new BoardConfig { Repos = entries }, Ct);
        var git = new CountingGit();
        var board = new BoardService(new GitClient(git), store, _time);
        git.Board = board;
        await board.LoadAsync(Ct);
        return (board, git);
    }

    /// <summary>Answers every call at once, except a fetch or pull, which takes a moment and is counted.</summary>
    private sealed class CountingGit : GitRunner
    {
        private readonly Lock _lock = new();
        private int _running;

        public BoardService Board { get; set; } = null!;

        /// <summary>What <c>git worktree list --porcelain -z</c> answers.</summary>
        public string Worktrees { get; set; } = "";
        public int Fetches { get; private set; }
        public int MostAtOnce { get; private set; }
        public int MostBusy { get; private set; }

        public override async Task<GitResult> RunAsync(string workingDirectory, IReadOnlyList<string> args, GitCallMode mode,
            TimeSpan timeout, string? input, CancellationToken cancellationToken)
        {
            if (args[0] == "rev-parse")
            {
                return new GitResult(0, Path.Combine(workingDirectory, ".git"), "");
            }

            if (args[0] == "worktree")
            {
                return new GitResult(0, Worktrees, "");
            }

            if (args[0] is not ("fetch" or "pull"))
            {
                return new GitResult(0, "", "");
            }

            lock (_lock)
            {
                Fetches++;
                MostAtOnce = Math.Max(MostAtOnce, ++_running);
                MostBusy = Math.Max(MostBusy, Board.Repos.Count(r => r.Busy is not null));
            }

            await Task.Delay(150, cancellationToken);
            lock (_lock)
            {
                _running--;
            }

            return new GitResult(0, "", "");
        }
    }

    public void Dispose() => _folder.Dispose();
}
