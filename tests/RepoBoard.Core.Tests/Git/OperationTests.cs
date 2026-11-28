using Microsoft.Extensions.Time.Testing;
using RepoBoard.Core.Board;
using RepoBoard.Core.Git;
using RepoBoard.Core.Repos;
using RepoBoard.Core.Resources;

namespace RepoBoard.Core.Tests.Git;

/// <summary>Merge and rebase, and finishing a stopped operation (WP-07, ADR 0014) against real git.</summary>
public sealed class OperationTests : IDisposable
{
    private readonly GitSandbox _git = new();
    private readonly GitClient _client = new(new GitRunner());
    private readonly FakeTimeProvider _time = new();
    private readonly BoardService _board;

    public OperationTests() =>
        _board = new(_client, new ConfigStore(Path.Combine(_git.Root, "config.json"), _time), _time);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Mine(string path) => Path.Combine(_git.Mine, path);

    private string Head(string rev = "HEAD") => GitSandbox.Git(_git.Mine, "rev-parse", rev).Trim();

    private async Task<RepoState> OpenMineAsync()
    {
        await _board.AddAsync([_git.Mine], Ct);
        var repo = _board.Repos.Single();
        await _board.SetWantsDetailsAsync(repo, true, Ct);
        return repo;
    }

    private static BranchInfo Branch(RepoState repo, string name) => repo.Details!.Branches.Single(b => b.Name == name);

    /// <summary>"feature" changes a.txt in <paramref name="commits"/> commits, main changes it too: each of them conflicts.</summary>
    private void Diverge(int commits = 1)
    {
        GitSandbox.Git(_git.Mine, "switch", "-c", "feature");
        for (var i = 1; i <= commits; i++)
        {
            GitSandbox.Commit(_git.Mine, "a.txt", $"feature {i}");
        }

        GitSandbox.Git(_git.Mine, "switch", "main");
        GitSandbox.Commit(_git.Mine, "a.txt", "main");
    }

    [Fact]
    public async Task A_merge_stops_at_a_conflict_and_continues_with_gits_message()
    {
        Diverge();
        var before = Head();
        var repo = await OpenMineAsync();

        Assert.False(await _board.MergeAsync(repo, Branch(repo, "feature"), Ct));

        var info = Assert.IsType<OperationInfo>(repo.Unfinished);
        Assert.Equal(GitOperation.Merge, info.Kind);
        Assert.Equal("Merge branch 'feature'", info.Subject); // the full ref still reads as the branch
        Assert.Equal(Head("feature"), info.Commit);
        Assert.Equal(1, repo.Status!.Conflicts);
        Assert.Contains(SyncText.Operation_Stopped.Split(':')[0], repo.Error, StringComparison.Ordinal);

        // Not with a conflict left; markers found until the file is resolved.
        Assert.False(await _board.ContinueAsync(repo, GitOperation.Merge, Ct));
        Assert.Contains(TextFormat.Format(SyncText.Continue_Conflicts, 1), repo.Error, StringComparison.Ordinal);
        Assert.True(await _board.HasConflictMarkersAsync(repo, "a.txt", Ct));
        File.WriteAllText(Mine("a.txt"), "both  \n"); // trailing blanks are no markers
        Assert.False(await _board.HasConflictMarkersAsync(repo, "a.txt", Ct));
        Assert.True(await _board.MarkResolvedAsync(repo, "a.txt", Ct), repo.Error);

        Assert.True(await _board.ContinueAsync(repo, GitOperation.Merge, Ct), repo.Error); // no editor waits (GIT_EDITOR)

        Assert.Null(repo.Unfinished);
        Assert.Null(repo.Error);
        Assert.Equal([before, Head("feature")], GitSandbox.Git(_git.Mine, "log", "-1", "--format=%P").Trim().Split(' '));
        Assert.Equal("Merge branch 'feature'", GitSandbox.Git(_git.Mine, "log", "-1", "--format=%s").Trim());
    }

    [Fact]
    public async Task A_rebase_shows_its_steps_and_goes_on_one_by_one()
    {
        Diverge(commits: 2);
        GitSandbox.Git(_git.Mine, "switch", "feature");
        var repo = await OpenMineAsync();

        Assert.False(await _board.RebaseAsync(repo, Branch(repo, "main"), Ct));

        var first = Assert.IsType<OperationInfo>(repo.Unfinished);
        Assert.Equal((GitOperation.Rebase, "feature", 1, 2, "a.txt: feature 1"), (first.Kind, first.Branch, first.Step, first.Steps, first.Subject));
        Assert.True(repo.Status!.IsDetached); // git rebases on a detached HEAD: the bar names the branch

        // A rebase goes on only with every change in the index.
        File.WriteAllText(Mine("a.txt"), "resolved 1");
        Assert.True(await _board.MarkResolvedAsync(repo, "a.txt", Ct), repo.Error);
        File.WriteAllText(Mine("a.txt"), "resolved 1, edited after");
        Assert.False(await _board.ContinueAsync(repo, GitOperation.Rebase, Ct));
        Assert.Contains(SyncText.Continue_Unstaged, repo.Error, StringComparison.Ordinal);
        Assert.True(await _board.MarkResolvedAsync(repo, "a.txt", Ct), repo.Error);

        // The next commit conflicts too: stopped again, one step further.
        Assert.False(await _board.ContinueAsync(repo, GitOperation.Rebase, Ct));
        var second = Assert.IsType<OperationInfo>(repo.Unfinished);
        Assert.Equal((2, "a.txt: feature 2"), (second.Step, second.Subject));
        Assert.NotEqual(first.Key, second.Key);

        // Skipping it with the key of the step before does nothing.
        Assert.False(await _board.SkipAsync(repo, first.Key, BoardService.DiscardedFiles(repo.Status!), Ct));
        Assert.Equal(second.Key, repo.Unfinished!.Key);

        Assert.True(await _board.SkipAsync(repo, second.Key, BoardService.DiscardedFiles(repo.Status!), Ct), repo.Error);

        Assert.Null(repo.Unfinished);
        Assert.Equal("feature", repo.Status!.Branch);
        Assert.Equal(["a.txt: feature 1", "a.txt: main"], GitSandbox.Git(_git.Mine, "log", "-2", "--format=%s").Trim().Split('\n'));
        Assert.Equal("resolved 1, edited after", File.ReadAllText(Mine("a.txt")));
    }

    [Fact]
    public async Task Abort_goes_back_only_for_the_operation_and_files_the_question_named()
    {
        Diverge();
        var before = Head();
        File.WriteAllText(Mine("new.txt"), "untracked, stays");
        var repo = await OpenMineAsync();
        Assert.False(await _board.MergeAsync(repo, Branch(repo, "feature"), Ct));
        var key = repo.Unfinished!.Key;
        IReadOnlyList<string> files = BoardService.DiscardedFiles(repo.Status!);
        Assert.Equal(["a.txt"], files);

        // Another session went on meanwhile: a new conflicted or changed file, or another operation.
        Assert.False(await _board.AbortAsync(repo, key, [], Ct));
        Assert.Contains(SyncText.Operation_FilesChanged, repo.Error, StringComparison.Ordinal);
        Assert.False(await _board.AbortAsync(repo, "Merge:0000", files, Ct));
        Assert.NotNull(repo.Unfinished);

        Assert.True(await _board.AbortAsync(repo, key, files, Ct), repo.Error);

        Assert.Null(repo.Unfinished);
        Assert.Equal(before, Head());
        Assert.Equal("main", File.ReadAllText(Mine("a.txt")));
        Assert.Equal("untracked, stays", File.ReadAllText(Mine("new.txt")));
    }

    [Fact]
    public async Task Nothing_to_continue_or_abort_once_the_terminal_finished_it()
    {
        Diverge();
        var repo = await OpenMineAsync();
        Assert.False(await _board.MergeAsync(repo, Branch(repo, "feature"), Ct));
        var key = repo.Unfinished!.Key;
        GitSandbox.Git(_git.Mine, "merge", "--abort");

        Assert.False(await _board.AbortAsync(repo, key, ["a.txt"], Ct));

        Assert.Contains(SyncText.Operation_Gone, repo.Error, StringComparison.Ordinal);
        Assert.Null(repo.Unfinished);
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("staged")]
    [InlineData("detached")]
    [InlineData("running")]
    public async Task A_merge_keeps_out_where_an_abort_could_lose_work(string state)
    {
        Diverge();
        switch (state)
        {
            case "changed":
                File.WriteAllText(Mine("a.txt"), "local edit");
                break;
            case "staged":
                File.WriteAllText(Mine("b.txt"), "staged");
                GitSandbox.Git(_git.Mine, "add", "b.txt");
                break;
            case "detached":
                GitSandbox.Git(_git.Mine, "switch", "--detach");
                break;
            case "running":
                GitSandbox.Git(_git.Mine, "switch", "-c", "other", "feature~1");
                GitSandbox.Commit(_git.Mine, "a.txt", "other");
                GitSandbox.TryGit(_git.Mine, "cherry-pick", "main");
                break;
        }

        var before = Head();
        var repo = await OpenMineAsync();

        Assert.False(await _board.MergeAsync(repo, Branch(repo, "feature"), Ct));

        Assert.Equal(before, Head());
        Assert.Contains(state switch
        {
            "detached" => SyncText.Blocker_Detached,
            "running" => TextFormat.Format(SyncText.Pick_Operation, SyncText.Operation_CherryPick),
            _ => SyncText.Pick_Changes,
        }, repo.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_merge_keeps_out_where_it_would_overwrite_an_ignored_file()
    {
        GitSandbox.Commit(_git.Mine, ".gitignore", "*.log\n");
        GitSandbox.Git(_git.Mine, "switch", "-c", "feature");
        File.WriteAllText(Mine("app.log"), "theirs");
        GitSandbox.Git(_git.Mine, "add", "-f", "app.log");
        GitSandbox.Git(_git.Mine, "commit", "-m", "force-add a log");
        GitSandbox.Git(_git.Mine, "switch", "main");
        File.WriteAllText(Mine("app.log"), "my local log");
        var before = Head();
        var repo = await OpenMineAsync();

        Assert.False(await _board.MergeAsync(repo, Branch(repo, "feature"), Ct));

        Assert.Contains(TextFormat.Format(SyncText.Pick_Ignored, "app.log"), repo.Error, StringComparison.Ordinal);
        Assert.Equal("my local log", File.ReadAllText(Mine("app.log")));
        Assert.Equal(before, Head());
    }

    [Fact]
    public async Task A_rebase_never_rewrites_pushed_commits_but_merges_a_remote_branch()
    {
        Diverge();
        GitSandbox.Git(_git.Mine, "push", "-q", "origin", "main");
        GitSandbox.Git(_git.Mine, "switch", "feature");
        GitSandbox.Git(_git.Mine, "push", "-q", "-u", "origin", "feature");
        GitSandbox.Git(_git.Mine, "fetch", "-q");
        var before = Head();
        var repo = await OpenMineAsync();

        Assert.False(await _board.RebaseAsync(repo, Branch(repo, "main"), Ct));

        Assert.Contains(TextFormat.Format(SyncText.Rebase_Pushed, TextFormat.Plural(1, SyncText.CommitsOne, SyncText.CommitsOther)),
            repo.Error, StringComparison.Ordinal);
        Assert.Equal(before, Head());

        // Merging the remote's main instead is fine: it rewrites nothing.
        GitSandbox.Git(_git.Mine, "switch", "main");
        GitSandbox.Git(_git.Mine, "reset", "-q", "--hard", "HEAD~1");
        GitSandbox.Git(_git.Mine, "switch", "-c", "topic");
        GitSandbox.Commit(_git.Mine, "t.txt", "topic");
        await _board.RefreshAsync(repo, Ct);
        Assert.True(await _board.MergeAsync(repo, new BranchInfo("origin/main", false, true, null, null, null), Ct), repo.Error);
        Assert.Equal("main", File.ReadAllText(Mine("a.txt")));
    }

    [Theory]
    [InlineData(GitOperation.CherryPick)]
    [InlineData(GitOperation.Revert)]
    public void The_git_folder_names_a_stopped_pick_and_its_commit(GitOperation kind)
    {
        GitSandbox.Commit(_git.Mine, "a.txt", "two");
        var picked = Head();
        if (kind == GitOperation.CherryPick)
        {
            GitSandbox.Git(_git.Mine, "switch", "-c", "other", "HEAD~1");
            GitSandbox.Commit(_git.Mine, "a.txt", "other");
            Assert.False(GitSandbox.TryGit(_git.Mine, "cherry-pick", picked));
        }
        else
        {
            GitSandbox.Commit(_git.Mine, "a.txt", "three");
            Assert.False(GitSandbox.TryGit(_git.Mine, "revert", "--no-edit", picked));
        }

        var info = OperationInfo.Read(Path.Combine(_git.Mine, ".git"));

        Assert.NotNull(info);
        Assert.Equal((kind, picked), (info.Kind, info.Commit));
        Assert.Equal(kind == GitOperation.CherryPick ? "a.txt: two" : "Revert \"a.txt: two\"", info.Subject);
    }

    public void Dispose() => _git.Dispose();
}
