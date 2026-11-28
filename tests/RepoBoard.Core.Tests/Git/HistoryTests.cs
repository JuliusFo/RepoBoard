using Microsoft.Extensions.Time.Testing;
using RepoBoard.Core.Board;
using RepoBoard.Core.Git;
using RepoBoard.Core.Repos;
using RepoBoard.Core.Resources;
using RepoBoard.Core.Sync;

namespace RepoBoard.Core.Tests.Git;

/// <summary>History, commit details and the commit tools (WP-09, ADR 0013) against real git.</summary>
public sealed class HistoryTests : IDisposable
{
    private readonly GitSandbox _git = new();
    private readonly GitClient _client = new(new GitRunner());
    private readonly FakeTimeProvider _time = new();
    private readonly BoardService _board;

    public HistoryTests() =>
        _board = Boards.New(_git.Root, _time);

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

    /// <summary>
    /// Review 0.4.0, A7: git writes the old separators (0x1E, 0x1F) in a subject and an author's name as they are – a
    /// stranger's commit added a made-up commit to the history and shifted its own fields, and its details did not open.
    /// </summary>
    [Fact]
    public async Task Separators_in_a_commit_s_text_neither_add_entries_nor_shift_fields()
    {
        var fake = new string('f', 40);
        GitSandbox.Git(_git.Mine, "commit", "--allow-empty", "--author=Ev\u001fil <e@x.invalid>",
            "-m", $"harmless\u001e{fake}\u001ffffffff\u001f\u001fMallory\u001f1 second ago\u001fHEAD -> main, tag: v9.9.9\u001fspoofed");
        var repo = await OpenMineAsync();

        var log = repo.Details!.Log;
        Assert.Equal(2, log.Count); // the sandbox's first commit and this one
        Assert.DoesNotContain(log, c => c.Id == fake || c.Author == "Mallory");
        Assert.Equal(("Ev\u001fil", Head()), (log[0].Author, log[0].Id));
        Assert.StartsWith("harmless", log[0].Subject, StringComparison.Ordinal);
        Assert.Equal("Ev\u001fil", (await DetailsAsync(repo, "HEAD")).Author);
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef01234567", true)]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", true)] // SHA-256
    [InlineData("0123abcd", false)]     // short: a branch "0123abcd" would win (review WP-09)
    [InlineData("0123456789abcdef0123456789abcdef0123456", false)]
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
            GitSandbox.Git(_git.Mine, "commit", "-q", "--allow-empty", "-m", $"n {i}"); // one process each, not add + commit (review 0.4.0, C14)
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

    /// <summary>
    /// Review 0.4.0, A13: "all branches" while a status read held the gate – the read was skipped, the box was ticked and the
    /// history still showed HEAD only, up to two minutes for a quiet repository.
    /// </summary>
    [Fact]
    public async Task All_branches_while_a_status_is_read_shows_once_the_read_is_done()
    {
        var feature = Feature();
        var repo = await OpenMineAsync();
        Assert.DoesNotContain(repo.Details!.Log, c => c.Id == feature);

        await repo.Gate.WaitAsync(Ct); // a status read of the file watcher
        var set = _board.SetLogAsync(repo, BoardService.LogLength, all: true, Ct);
        Assert.False(set.IsCompleted);
        repo.Gate.Release();
        await set;

        Assert.Contains(repo.Details!.Log, c => c.Id == feature);
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

    [Fact]
    public async Task A_change_picked_before_is_not_picked_again()
    {
        Feature();
        GitSandbox.Commit(_git.Mine, "b.txt", "on main");
        var repo = await OpenMineAsync();
        var commit = await DetailsAsync(repo, "feature");
        Assert.True(await _board.CherryPickAsync(repo, commit, Ct));
        var picked = Head();

        commit = await DetailsAsync(repo, "feature");
        Assert.True(await _board.IsAppliedAsync(repo, commit, Ct));
        Assert.False(await _board.CherryPickAsync(repo, commit, Ct));

        // git would have stopped with an empty pick and left it unfinished (E2E WP-09).
        Assert.Null(repo.Operation);
        Assert.Equal(picked, Head());
        Assert.Contains(SyncText.CherryPick_Applied, repo.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Each with the board's own reason: git itself refuses "staged", "merge" and "not in head" and leaves "in head" as an
    /// unfinished cherry-pick – a test that only wanted "some error" stayed green without the board's checks (review 0.4.0, C4).
    /// </summary>
    public static TheoryData<string, bool, string> Refusals => new()
    {
        { "changed", true, SyncText.Pick_Changes },
        { "staged", true, SyncText.Pick_Changes },
        { "merge", true, SyncText.Pick_Merge },
        { "not in head", true, SyncText.Revert_NotInHead },
        { "in head", false, SyncText.CherryPick_InHead },
        { "operation", true, TextFormat.Format(SyncText.Pick_Operation, PullCheck.Name(GitOperation.Merge)) },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Revert_and_cherry_pick_keep_out_where_they_could_mix_with_work(string situation, bool revert, string reason)
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
        Assert.Contains(reason, repo.Error, StringComparison.Ordinal);
        Assert.Equal(situation == "operation" ? GitOperation.Merge : null, repo.Operation); // no pick left unfinished
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
    public async Task A_conflict_leaves_the_cherry_pick_unfinished_and_says_how_to_go_on()
    {
        Feature("a.txt", "feature's line");
        GitSandbox.Commit(_git.Mine, "a.txt", "main's line");
        var repo = await OpenMineAsync();
        var commit = await DetailsAsync(repo, "feature");

        Assert.False(await _board.CherryPickAsync(repo, commit, Ct));

        Assert.Equal(GitOperation.CherryPick, repo.Operation);
        Assert.Equal(commit.Id, repo.Unfinished!.Commit);
        Assert.Equal(1, repo.Status!.Conflicts);
        Assert.Contains(SyncText.Operation_Stopped.Split(':')[0], repo.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_ignored_file_at_a_path_of_the_commit_keeps_revert_and_cherry_pick_out(bool revert)
    {
        // Review WP-09: git counts ignored files as expendable and overwrote the user's .env without a word.
        File.WriteAllText(Mine(".gitignore"), ".env\n");
        GitSandbox.Git(_git.Mine, "add", ".gitignore");
        GitSandbox.Git(_git.Mine, "commit", "-m", "ignore .env");
        if (revert)
        {
            File.WriteAllText(Mine(".env"), "SECRET=old");
            GitSandbox.Git(_git.Mine, "add", "-f", ".env");
            GitSandbox.Git(_git.Mine, "commit", "-m", "track .env");
            GitSandbox.Git(_git.Mine, "rm", "-q", "--cached", ".env");
            GitSandbox.Git(_git.Mine, "commit", "-m", "stop tracking .env");
        }
        else
        {
            GitSandbox.Git(_git.Mine, "switch", "-c", "feature");
            File.WriteAllText(Mine(".env"), "SECRET=theirs");
            GitSandbox.Git(_git.Mine, "add", "-f", ".env");
            GitSandbox.Git(_git.Mine, "commit", "-m", "add .env");
            GitSandbox.Git(_git.Mine, "switch", "main");
        }

        File.WriteAllText(Mine(".env"), "SECRET=mine");
        var before = Head();
        var repo = await OpenMineAsync();
        var commit = await DetailsAsync(repo, revert ? "HEAD" : "feature");

        Assert.False(revert ? await _board.RevertAsync(repo, commit, Ct) : await _board.CherryPickAsync(repo, commit, Ct));

        Assert.Equal("SECRET=mine", File.ReadAllText(Mine(".env")));
        Assert.Equal(before, Head());
        Assert.Contains(".env", repo.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_ignored_file_where_the_commit_needs_a_folder_keeps_the_cherry_pick_out()
    {
        // Second review pass WP-09: the file "foo" became a folder for "foo/bar" and was gone.
        File.WriteAllText(Mine(".gitignore"), "foo\nnode_modules/\n");
        GitSandbox.Git(_git.Mine, "add", ".gitignore");
        GitSandbox.Git(_git.Mine, "commit", "-m", "ignore");
        GitSandbox.Git(_git.Mine, "switch", "-c", "feature");
        Directory.CreateDirectory(Mine("foo"));
        File.WriteAllText(Mine(Path.Combine("foo", "bar")), "theirs");
        GitSandbox.Git(_git.Mine, "add", "-f", "foo/bar");
        GitSandbox.Git(_git.Mine, "commit", "-m", "add foo/bar");
        GitSandbox.Git(_git.Mine, "switch", "main");
        File.WriteAllText(Mine("foo"), "mine");
        var before = Head();
        var repo = await OpenMineAsync();

        Assert.False(await _board.CherryPickAsync(repo, await DetailsAsync(repo, "feature"), Ct));

        Assert.Equal("mine", File.ReadAllText(Mine("foo")));
        Assert.Equal(before, Head());
        Assert.Contains("foo", repo.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_ignored_folder_above_the_commits_paths_is_no_obstacle()
    {
        File.WriteAllText(Mine(".gitignore"), "build/\n");
        GitSandbox.Git(_git.Mine, "add", ".gitignore");
        GitSandbox.Git(_git.Mine, "commit", "-m", "ignore");
        GitSandbox.Git(_git.Mine, "switch", "-c", "feature");
        Directory.CreateDirectory(Mine("build"));
        File.WriteAllText(Mine(Path.Combine("build", "kept.txt")), "tracked on purpose");
        GitSandbox.Git(_git.Mine, "add", "-f", "build/kept.txt");
        GitSandbox.Git(_git.Mine, "commit", "-m", "add build/kept.txt");
        GitSandbox.Git(_git.Mine, "switch", "main");
        GitSandbox.Commit(_git.Mine, "b.txt", "main moves on");
        Directory.CreateDirectory(Mine("build"));
        File.WriteAllText(Mine(Path.Combine("build", "output.dll")), "local build output");
        var repo = await OpenMineAsync();

        Assert.True(await _board.CherryPickAsync(repo, await DetailsAsync(repo, "feature"), Ct), repo.Error);

        Assert.Equal("local build output", File.ReadAllText(Mine(Path.Combine("build", "output.dll"))));
    }

    [Fact]
    public async Task Git_itself_keeps_a_new_file_that_a_cherry_pick_would_bring()
    {
        Feature("f.txt", "theirs");
        File.WriteAllText(Mine("f.txt"), "mine, not ignored");
        var before = Head();
        var repo = await OpenMineAsync();

        Assert.False(await _board.CherryPickAsync(repo, await DetailsAsync(repo, "feature"), Ct));

        Assert.Equal("mine, not ignored", File.ReadAllText(Mine("f.txt")));
        Assert.Equal(before, Head());
        Assert.Null(repo.Operation);
    }

    [Fact]
    public async Task A_cherry_pick_that_comes_out_empty_after_a_squash_says_so()
    {
        // Review WP-09: two commits squashed into one have another patch id than each of them, "git cherry" does not see it.
        var first = Feature();
        GitSandbox.Git(_git.Mine, "switch", "feature");
        GitSandbox.Commit(_git.Mine, "g.txt", "second");
        GitSandbox.Git(_git.Mine, "switch", "main");
        GitSandbox.Git(_git.Mine, "merge", "--squash", "feature");
        GitSandbox.Git(_git.Mine, "commit", "-m", "squashed feature");
        var repo = await OpenMineAsync();
        var commit = await DetailsAsync(repo, first);
        Assert.False(await _board.IsAppliedAsync(repo, commit, Ct));

        Assert.False(await _board.CherryPickAsync(repo, commit, Ct));

        Assert.Equal(GitOperation.CherryPick, repo.Operation); // never skipped on its own (decision of the user)
        Assert.Contains(SyncText.CherryPick_Empty, repo.Error, StringComparison.Ordinal);

        // "Skip", as the message says (WP-07).
        Assert.True(await _board.SkipAsync(repo, repo.Unfinished!.Key, [], Ct), repo.Error);
        Assert.Null(repo.Operation);
    }

    [Fact]
    public async Task Amend_keeps_out_when_another_commit_came_since_its_message_was_filled_in()
    {
        GitSandbox.Commit(_git.Mine, "b.txt", "mine");
        var filledIn = Head();
        var repo = await OpenMineAsync();
        GitSandbox.Commit(_git.Mine, "c.txt", "another session"); // review WP-09
        var other = Head();

        Assert.False(await _board.CommitAsync(repo, "b.txt: mine, better", push: false, Ct, amend: true, expectedHead: filledIn));

        Assert.Equal(other, Head());
        Assert.Equal("c.txt: another session", GitSandbox.Git(_git.Mine, "log", "-1", "--format=%s").Trim());
        Assert.Contains(SyncText.Amend_HeadMoved, repo.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Amend_keeps_out_while_a_rebase_is_stopped()
    {
        GitSandbox.Commit(_git.Mine, "b.txt", "mine");
        var before = Head();
        var repo = await OpenMineAsync();
        Directory.CreateDirectory(Path.Combine(_git.Mine, ".git", "rebase-merge")); // review WP-09: git would amend

        Assert.False(await _board.CommitAsync(repo, "rewritten", push: false, Ct, amend: true, expectedHead: before));

        Assert.Equal(before, Head());
        Assert.NotNull(repo.Error);
    }

    [Fact]
    public void Paths_go_to_git_in_batches_below_the_command_line_limit()
    {
        string[] paths = [.. Enumerable.Range(0, 1000).Select(i => $"folder/file-{i:D4}.txt")];

        var batches = GitClient.Batches(paths, 2000).ToList();

        Assert.Equal(paths, batches.SelectMany(b => b));
        Assert.All(batches, b => Assert.True(b.Sum(p => p.Length + 1) <= 2000));
        Assert.Equal([["a very long path"]], GitClient.Batches(["a very long path"], 5)); // one path is never split off
    }

    public void Dispose() => _git.Dispose();
}
