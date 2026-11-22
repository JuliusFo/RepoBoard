using Microsoft.Extensions.Time.Testing;
using RepoBoard.Core.Board;
using RepoBoard.Core.Git;
using RepoBoard.Core.Repos;
using RepoBoard.Core.Resources;

namespace RepoBoard.Core.Tests.Git;

/// <summary>History, commit details and the commit tools (WP-09, ADR 0013) against real git.</summary>
public sealed class HistoryTests : IDisposable
{
    private readonly GitSandbox _git = new();
    private readonly GitClient _client = new(new GitRunner());
    private readonly FakeTimeProvider _time = new();
    private readonly BoardService _board;

    public HistoryTests() =>
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

    private async Task<CommitDetails> DetailsAsync(RepoState repo, string rev)
    {
        var (commit, error) = await _board.CommitDetailsAsync(repo, Head(rev), Ct);
        Assert.True(commit is not null, error);
        return commit;
    }

    /// <summary>A branch "feature" off main with one commit, Mine back on main.</summary>
    private string Feature(string file = "f.txt", string content = "feature")
    {
        GitSandbox.Git(_git.Mine, "switch", "-c", "feature");
        GitSandbox.Commit(_git.Mine, file, content);
        var id = Head();
        GitSandbox.Git(_git.Mine, "switch", "main");
        return id;
    }

    [Theory]
    [InlineData("0123abcd", true)]
    [InlineData("0123456789abcdef0123456789abcdef01234567", true)]
    [InlineData("abc", false)]          // too short to be unique
    [InlineData("-abcdef", false)]      // an option
    [InlineData("HEAD~1", false)]       // a revision, not an id
    [InlineData("a..b0", false)]        // a range
    [InlineData("0123ABCD", false)]     // git writes ids in lower case
    public void Only_hex_ids_are_commit_ids(string id, bool valid) => Assert.Equal(valid, GitClient.IsCommitId(id));

    [Fact]
    public async Task The_log_tells_whether_more_follows_and_shows_other_branches_on_request()
    {
        GitSandbox.Commit(_git.Mine, "b.txt", "two");
        GitSandbox.Commit(_git.Mine, "c.txt", "three");
        var feature = Feature();
        File.WriteAllText(Mine("HEAD"), "a file named like a ref"); // must not make the revision ambiguous

        var (two, more) = await _client.LogAsync(_git.Mine, 2, all: false, Ct);
        var (three, none) = await _client.LogAsync(_git.Mine, 3, all: false, Ct);
        var (all, _) = await _client.LogAsync(_git.Mine, 10, all: true, Ct);

        Assert.Equal((2, true), (two.Count, more));
        Assert.Equal((3, false), (three.Count, none));
        Assert.Equal(Head(), two[0].Id);
        Assert.Equal([Head("HEAD~1")], two[0].Parents);
        Assert.DoesNotContain(three, c => c.Id == feature);
        Assert.Equal(feature, all[0].Id); // newest first, the feature's commit before main's
    }

    [Fact]
    public async Task Details_have_the_whole_message_and_every_kind_of_file()
    {
        File.WriteAllText(Mine("b.txt"), "to delete");
        File.WriteAllText(Mine("old name.txt"), string.Join('\n', Enumerable.Range(1, 20).Select(n => $"line {n}")));
        GitSandbox.Git(_git.Mine, "add", "-A");
        GitSandbox.Git(_git.Mine, "commit", "-m", "prepare");
        GitSandbox.Git(_git.Mine, "mv", "old name.txt", "new näme.txt");
        GitSandbox.Git(_git.Mine, "rm", "-q", "b.txt");
        File.WriteAllText(Mine("a.txt"), "changed");
        File.WriteAllText(Mine("c.txt"), "new");
        GitSandbox.Git(_git.Mine, "add", "-A");
        GitSandbox.Git(_git.Mine, "commit", "-m", "Subject line\n\nBody with\u001f a separator.");
        var repo = await OpenMineAsync();

        var commit = await DetailsAsync(repo, "HEAD");

        Assert.Equal("Subject line", commit.Subject);
        Assert.Equal("Subject line\n\nBody with\u001f a separator.", commit.Message);
        Assert.Equal(("Test", "test@example.invalid"), (commit.Author, commit.AuthorEmail));
        Assert.Equal([Head("HEAD~1")], commit.Parents);
        Assert.Contains("main", commit.Refs, StringComparison.Ordinal);
        Assert.True(commit.InHead);
        Assert.Equal(
            [new CommitFile("a.txt", null, 'M'), new CommitFile("b.txt", null, 'D'), new CommitFile("c.txt", null, 'A'),
             new CommitFile("new näme.txt", "old name.txt", 'R')],
            commit.Files.OrderBy(f => f.Path, StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_files_diff_is_against_the_parent_and_a_rename_is_one_file()
    {
        File.WriteAllText(Mine("old.txt"), string.Join('\n', Enumerable.Range(1, 20).Select(n => $"line {n}")) + "\n");
        GitSandbox.Git(_git.Mine, "add", "-A");
        GitSandbox.Git(_git.Mine, "commit", "-m", "prepare");
        GitSandbox.Git(_git.Mine, "mv", "old.txt", "new.txt");
        File.WriteAllText(Mine("new.txt"), string.Join('\n', Enumerable.Range(1, 20).Select(n => n == 5 ? "line five" : $"line {n}")) + "\n");
        GitSandbox.Git(_git.Mine, "add", "-A");
        GitSandbox.Git(_git.Mine, "commit", "-m", "rename");
        var repo = await OpenMineAsync();
        var commit = await DetailsAsync(repo, "HEAD");

        var (diff, error) = await _board.CommitDiffAsync(repo, commit, commit.Files.Single(), Ct);

        Assert.True(diff is not null, error);
        Assert.False(diff.IsTwoFiles);
        var hunk = Assert.Single(diff.Hunks);
        Assert.Equal(["line 5"], hunk.Lines.Where(l => l.Kind == DiffLineKind.Removed).Select(l => l.Text));
        Assert.Equal(["line five"], hunk.Lines.Where(l => l.Kind == DiffLineKind.Added).Select(l => l.Text));
    }

    [Fact]
    public async Task A_root_commit_shows_its_files_as_added_and_a_merge_what_it_brought()
    {
        var feature = Feature();
        GitSandbox.Commit(_git.Mine, "m.txt", "main");
        GitSandbox.Git(_git.Mine, "merge", "--no-edit", "--no-ff", "feature");
        var repo = await OpenMineAsync();

        var root = await DetailsAsync(repo, "HEAD~2");
        var merge = await DetailsAsync(repo, "HEAD");

        Assert.True(root.IsRoot);
        Assert.Equal([new CommitFile("a.txt", null, 'A')], root.Files);
        Assert.True(merge.IsMerge);
        Assert.Equal(feature, merge.Parents[1]);
        Assert.Equal([new CommitFile("f.txt", null, 'A')], merge.Files); // against the first parent
        var (diff, _) = await _board.CommitDiffAsync(repo, merge, merge.Files[0], Ct);
        Assert.Equal(["feature"], diff!.Hunks.Single().Lines.Select(l => l.Text));
    }

    [Fact]
    public async Task A_file_above_the_limit_is_not_read()
    {
        File.WriteAllText(Mine("big.txt"), new string('x', (int)FileDiff.MaxFileBytes + 1));
        GitSandbox.Git(_git.Mine, "add", "-A");
        GitSandbox.Git(_git.Mine, "commit", "-m", "big");
        var repo = await OpenMineAsync();
        var commit = await DetailsAsync(repo, "HEAD");

        var (diff, _) = await _board.CommitDiffAsync(repo, commit, commit.Files.Single(), Ct);

        Assert.True(diff!.IsTooLarge);
    }

    [Fact]
    public async Task The_opened_row_reads_more_and_all_branches_with_a_graph()
    {
        for (var i = 0; i < 20; i++)
        {
            GitSandbox.Commit(_git.Mine, "n.txt", $"{i}");
        }

        var repo = await OpenMineAsync();
        Assert.Equal((BoardService.LogLength, true), (repo.Details!.Log.Count, repo.Details.HasMore));
        Assert.Equal(repo.Details.Log.Count, repo.Details.Graph.Count);

        var feature = Feature();
        GitSandbox.Commit(_git.Mine, "n.txt", "after the feature forked");
        await _board.SetLogAsync(repo, BoardService.LogLength + BoardService.LogStep, all: true, Ct);

        Assert.Equal((23, false), (repo.Details.Log.Count, repo.Details.HasMore));
        Assert.Contains(repo.Details.Log, c => c.Id == feature);
        Assert.Contains(repo.Details.Graph, r => r.Width == 2); // the feature beside main

        await _board.SetWantsDetailsAsync(repo, false, Ct);
        Assert.Equal((BoardService.LogLength, true), (repo.LogLength, repo.LogAll)); // "all" stays, the length does not
    }

    // ---------- amend ----------

    [Fact]
    public async Task Amend_replaces_an_unpushed_commit_with_the_staged_changes_and_new_message()
    {
        GitSandbox.Commit(_git.Mine, "b.txt", "first");
        var before = Head();
        var repo = await OpenMineAsync();
        Assert.False(repo.Details!.HeadOnRemote);
        Assert.Equal("b.txt: first", await _board.HeadMessageAsync(repo, Ct));
        File.WriteAllText(Mine("b.txt"), "second");
        File.WriteAllText(Mine("c.txt"), "not staged");
        GitSandbox.Git(_git.Mine, "add", "b.txt");

        Assert.True(await _board.CommitAsync(repo, "Better message", push: false, Ct, amend: true));

        Assert.NotEqual(before, Head());
        Assert.Equal(Head("origin/main"), Head("HEAD~1")); // replaced, not added
        Assert.Equal("Better message", GitSandbox.Git(_git.Mine, "log", "-1", "--format=%B").Trim());
        Assert.Equal("second", GitSandbox.Git(_git.Mine, "show", "HEAD:b.txt"));
        Assert.Contains(repo.Status!.Changes, c => c.Path == "c.txt" && c.Untracked); // nothing staged on top
    }

    [Fact]
    public async Task Amend_refuses_a_pushed_commit()
    {
        var repo = await OpenMineAsync(); // HEAD is origin/main
        Assert.True(repo.Details!.HeadOnRemote);
        var before = Head();

        Assert.False(await _board.CommitAsync(repo, "Rewrite history", push: false, Ct, amend: true));

        Assert.Equal(before, Head());
        Assert.Contains(SyncText.Amend_Pushed, repo.Error, StringComparison.Ordinal);
    }

    // ---------- revert and cherry-pick ----------

    [Fact]
    public async Task Revert_adds_a_commit_that_undoes_one_of_the_branch()
    {
        GitSandbox.Commit(_git.Mine, "b.txt", "oops");
        var repo = await OpenMineAsync();
        var commit = await DetailsAsync(repo, "HEAD");

        Assert.True(await _board.RevertAsync(repo, commit, Ct));

        Assert.Equal(commit.Id, Head("HEAD~1"));
        Assert.False(File.Exists(Mine("b.txt")));
        Assert.Null(repo.Error);
    }

    [Fact]
    public async Task Cherry_pick_brings_a_commit_of_another_branch_onto_the_current_one()
    {
        Feature();
        // Main moves on: picked onto the feature's own parent within the same second, git would make the very same commit.
        GitSandbox.Commit(_git.Mine, "b.txt", "on main");
        var before = Head();
        var repo = await OpenMineAsync();
        var commit = await DetailsAsync(repo, "feature");
        Assert.False(commit.InHead);

        Assert.True(await _board.CherryPickAsync(repo, commit, Ct));

        Assert.Equal("feature", File.ReadAllText(Mine("f.txt")));
        Assert.Equal(before, Head("HEAD~1")); // a new commit with the same change on top of main
    }

    public static TheoryData<string, bool> Refusals => new()
    {
        { "changed", true },
        { "staged", true },
        { "merge", true },
        { "not in head", true },
        { "in head", false },
        { "operation", true },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Revert_and_cherry_pick_keep_out_where_they_could_mix_with_work(string situation, bool revert)
    {
        var feature = Feature();
        GitSandbox.Commit(_git.Mine, "b.txt", "on main");
        var repo = await OpenMineAsync();
        var commit = await DetailsAsync(repo, situation == "not in head" ? "feature" : "HEAD");
        switch (situation)
        {
            case "changed":
                File.WriteAllText(Mine("a.txt"), "work in progress");
                break;
            case "staged":
                File.WriteAllText(Mine("a.txt"), "work in progress");
                GitSandbox.Git(_git.Mine, "add", "a.txt");
                break;
            case "merge":
                GitSandbox.Git(_git.Mine, "merge", "--no-edit", "--no-ff", "feature");
                commit = await DetailsAsync(repo, "HEAD");
                break;
            case "operation":
                File.WriteAllText(Path.Combine(_git.Mine, ".git", "MERGE_HEAD"), feature + "\n");
                break;
        }

        var before = Head();
        var a = File.ReadAllText(Mine("a.txt"));

        Assert.False(revert ? await _board.RevertAsync(repo, commit, Ct) : await _board.CherryPickAsync(repo, commit, Ct));

        Assert.Equal(before, Head());
        Assert.Equal(a, File.ReadAllText(Mine("a.txt")));
        Assert.NotNull(repo.Error);
    }

    [Fact]
    public async Task New_files_do_not_keep_a_revert_out()
    {
        GitSandbox.Commit(_git.Mine, "b.txt", "oops");
        File.WriteAllText(Mine("untracked.txt"), "mine");
        var repo = await OpenMineAsync();

        Assert.True(await _board.RevertAsync(repo, await DetailsAsync(repo, "HEAD"), Ct));
        Assert.Equal("mine", File.ReadAllText(Mine("untracked.txt")));
    }

    [Fact]
    public async Task A_conflict_leaves_the_cherry_pick_unfinished_for_the_ide_and_says_so()
    {
        Feature("a.txt", "feature's line");
        GitSandbox.Commit(_git.Mine, "a.txt", "main's line");
        var repo = await OpenMineAsync();
        var commit = await DetailsAsync(repo, "feature");

        Assert.False(await _board.CherryPickAsync(repo, commit, Ct));

        Assert.Equal(GitOperation.CherryPick, repo.Operation);
        Assert.Equal(1, repo.Status!.Conflicts);
        Assert.Contains("cherry-pick --continue", repo.Error, StringComparison.Ordinal);
    }

    public void Dispose() => _git.Dispose();
}
