using Microsoft.Extensions.Time.Testing;
using RepoBoard.Core.Board;
using RepoBoard.Core.Git;
using RepoBoard.Core.Repos;

namespace RepoBoard.Core.Tests.Board;

/// <summary>Linked worktrees under their repository (WP-04) and removing them without losing work (ADR 0010).</summary>
public sealed class WorktreeTests : IDisposable
{
    private readonly GitSandbox _git = new();
    private readonly FakeTimeProvider _time = new();
    private readonly BoardService _board;

    public WorktreeTests() =>
        _board = new(new GitClient(new GitRunner()), new ConfigStore(Path.Combine(_git.Root, "config.json"), _time), _time);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<RepoState> AddMineAsync()
    {
        await _board.AddAsync([_git.Mine], Ct);
        return _board.Repos.Single();
    }

    /// <summary>A linked worktree of <see cref="GitSandbox.Mine"/> beside it (or at <paramref name="path"/>).</summary>
    private string AddWorktree(string name, string? path = null, params string[] options)
    {
        path ??= Path.Combine(_git.Root, name);
        GitSandbox.Git(_git.Mine, ["worktree", "add", .. options, path]);
        return path;
    }

    /// <summary>A worktree on a new branch that follows origin/main, like a second checkout of the default branch.</summary>
    private string AddFollowingWorktree(string name)
    {
        var path = Path.Combine(_git.Root, name);
        GitSandbox.Git(_git.Mine, "worktree", "add", "--track", "-b", name, path, "origin/main");
        return path;
    }

    private static RepoState Named(RepoState repo, string name) => repo.Worktrees.Single(w => w.Name == name);

    private Task<WorktreeCandidate> AskAsync(RepoState repo, string name) => _board.InspectWorktreeAsync(Named(repo, name), Ct);

    /// <summary>The main branch moves on, as when someone else's pull request is merged.</summary>
    private void MainMovesOn()
    {
        GitSandbox.Git(_git.Theirs, "pull", "--ff-only");
        GitSandbox.Commit(_git.Theirs, "later.txt", "later");
        GitSandbox.Git(_git.Theirs, "push");
        GitSandbox.Git(_git.Mine, "fetch", "--prune");
    }

    [Theory]
    [InlineData("worktree /r\0HEAD 1111111111111111111111111111111111111111\0branch refs/heads/main\0\0", "main", null, null, false)]
    [InlineData("worktree /r\0HEAD 1111111111111111111111111111111111111111\0detached\0\0", null, null, null, false)]
    [InlineData("worktree /r\0HEAD 1111111111111111111111111111111111111111\0branch refs/heads/f\0locked usb stick\0\0", "f", "usb stick", null, false)]
    [InlineData("worktree /r\0HEAD 1111111111111111111111111111111111111111\0branch refs/heads/f\0locked\0\0", "f", "", null, false)]
    [InlineData("worktree /r\0HEAD 1111111111111111111111111111111111111111\0branch refs/heads/f\0prunable gitdir file points to non-existent location\0\0", "f", null, "gitdir file points to non-existent location", false)]
    [InlineData("worktree /r\0bare\0\0", null, null, null, true)]
    // Review WP-04: a broken entry (empty gitdir file) has no path – left out instead of throwing for every repository.
    [InlineData("worktree \0HEAD 1111111111111111111111111111111111111111\0detached\0\0worktree /r\0bare\0\0", null, null, null, true)]
    public void The_porcelain_list_is_read_field_by_field(string output, string? branch, string? locked, string? prunable, bool bare)
    {
        var worktree = Assert.Single(WorktreeInfo.ParseList(Path.DirectorySeparatorChar == '\\' ? output.Replace("/r", "C:/r", StringComparison.Ordinal) : output));

        Assert.Equal(Path.GetFullPath(Path.DirectorySeparatorChar == '\\' ? "C:/r" : "/r"), worktree.Path);
        Assert.Equal(branch, worktree.Branch);
        Assert.Equal(locked, worktree.Locked);
        Assert.Equal(prunable, worktree.Prunable);
        Assert.Equal(bare, worktree.IsBare);
    }

    [Fact]
    public async Task Linked_worktrees_show_under_their_repository_with_their_own_state()
    {
        var feature = AddWorktree("feature", null, "-b", "feature");
        AddWorktree("c1", Path.Combine(_git.Mine, ".claude", "worktrees", "c1"), "--detach");
        File.WriteAllText(Path.Combine(feature, "new.txt"), "x");

        var repo = await AddMineAsync();

        Assert.Equal(["c1", "feature"], repo.Worktrees.Select(w => w.Name).Order(StringComparer.Ordinal));
        var worktree = Named(repo, "feature");
        Assert.Same(repo, worktree.MainRepo);
        Assert.Equal("feature", worktree.Status!.Branch);
        Assert.Equal(1, worktree.Status.Untracked);
        Assert.False(worktree.AutoSyncOn);
        Assert.False(worktree.IsClaudeWorktree);
        Assert.True(Named(repo, "c1").IsClaudeWorktree);
        Assert.True(Named(repo, "c1").Status!.IsDetached);

        // A new status keeps the worktree's state object (the opened row, its gate) and drops what git no longer lists.
        GitSandbox.Git(_git.Mine, "worktree", "remove", "--force", Path.Combine(_git.Mine, ".claude", "worktrees", "c1"));
        await _board.RefreshAllAsync(Ct);
        Assert.Same(worktree, Assert.Single(repo.Worktrees));
        Assert.Single(_board.Repos); // never in the configuration
    }

    /// <summary>
    /// Review WP-04: a submodule's git folder is git's "main worktree" of it – the submodule stays as chosen, and so does a
    /// linked worktree of it (second review pass): never a git folder on the board.
    /// </summary>
    [Fact]
    public async Task Adding_a_worktree_puts_its_repository_on_the_board_but_a_submodule_stays()
    {
        var feature = AddWorktree("feature", null, "-b", "feature");
        var sub = Path.Combine(_git.Theirs, "sub");
        GitSandbox.Git(_git.Theirs, "-c", "protocol.file.allow=always", "submodule", "add", _git.Remote, "sub");
        var subWorktree = Path.Combine(_git.Root, "subwt");
        GitSandbox.Git(sub, "worktree", "add", "-b", "subwt", subWorktree);

        await _board.AddAsync([feature, sub, subWorktree], Ct);

        Assert.Equal(["mine", "sub", "subwt"], _board.Repos.Select(r => r.Name));
        Assert.Equal("feature", Assert.Single(_board.Repos[0].Worktrees).Name);
        Assert.Equal(sub, _board.Repos[1].Path, ignoreCase: OperatingSystem.IsWindows());
        Assert.Equal(subWorktree, _board.Repos[2].Path, ignoreCase: OperatingSystem.IsWindows());
    }

    /// <summary>Review WP-04: worktrees found by a scan bring their repository, also from outside the scanned folder.</summary>
    [Fact]
    public async Task A_scan_puts_the_repository_of_the_worktrees_it_finds_on_the_board()
    {
        var elsewhere = Path.Combine(_git.Root, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        AddWorktree("one", Path.Combine(elsewhere, "one"), "-b", "one");
        AddWorktree("two", Path.Combine(elsewhere, "two"), "-b", "two");

        await _board.ScanAsync(elsewhere, Ct);

        var repo = Assert.Single(_board.Repos);
        Assert.Equal("mine", repo.Name);
        Assert.Equal(["one", "two"], repo.Worktrees.Select(w => w.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Removing_a_clean_worktree_keeps_its_branch()
    {
        var feature = AddWorktree("feature", null, "-b", "feature");
        var repo = await AddMineAsync();

        Assert.True(await _board.RemoveWorktreeAsync(await AskAsync(repo, "feature"), Ct));

        Assert.False(Directory.Exists(feature));
        Assert.Empty(repo.Worktrees);
        Assert.Contains("refs/heads/feature", GitSandbox.Git(_git.Mine, "for-each-ref", "--format=%(refname)", "refs/heads"), StringComparison.Ordinal);
        Assert.Contains(_board.Activity.Entries, e => e.Text.EndsWith(" entfernt; Branch feature bleibt.", StringComparison.Ordinal));
    }

    /// <summary>
    /// ADR 0010: a worktree with work in it is never removed – also when the work started after the question showed it
    /// clean (review WP-04: a rebase stopped at "edit" leaves the tree clean).
    /// </summary>
    [Theory]
    [InlineData("new", WorktreeBlock.Changes, "geänderte oder neue Dateien")]
    [InlineData("modified", WorktreeBlock.Changes, "geänderte oder neue Dateien")]
    [InlineData("staged", WorktreeBlock.Changes, "geänderte oder neue Dateien")]
    [InlineData("rebase", WorktreeBlock.Operation, "Merge oder Rebase")]
    public async Task A_worktree_with_work_in_it_stays(string work, WorktreeBlock block, string text)
    {
        var feature = AddWorktree("feature", null, "-b", "feature");
        GitSandbox.Commit(feature, "f.txt", "f");
        var repo = await AddMineAsync();
        var question = await AskAsync(repo, "feature");
        Assert.True(question.CanRemove);

        var file = Path.Combine(feature, work == "new" ? "new.txt" : "a.txt");
        if (work == "rebase")
        {
            GitSandbox.Git(feature, "-c", "sequence.editor=sed -i -e s/^pick/edit/", "rebase", "-i", "HEAD~1");
        }
        else
        {
            File.WriteAllText(file, "work");
            if (work == "staged")
            {
                GitSandbox.Git(feature, "add", "-A");
            }
        }

        Assert.False(await _board.RemoveWorktreeAsync(question, Ct));

        Assert.True(Directory.Exists(Path.Combine(feature, ".git")) || File.Exists(Path.Combine(feature, ".git")));
        var worktree = Named(repo, "feature");
        Assert.Contains(text, worktree.Error, StringComparison.Ordinal);
        Assert.Equal(block, (await AskAsync(repo, "feature")).Block);
        if (work != "rebase")
        {
            Assert.Equal("work", File.ReadAllText(file));
        }
    }

    [Fact]
    public async Task A_locked_worktree_stays()
    {
        var feature = AddWorktree("feature", null, "-b", "feature");
        GitSandbox.Git(_git.Mine, "worktree", "lock", "--reason", "usb stick", feature);
        var repo = await AddMineAsync();

        var question = await AskAsync(repo, "feature");
        Assert.Equal("usb stick", question.Worktree.Worktree!.Locked);
        Assert.Equal(WorktreeBlock.Locked, question.Block);
        Assert.False(await _board.RemoveWorktreeAsync(question, Ct));
        Assert.True(Directory.Exists(feature));
    }

    /// <summary>
    /// Review WP-04: commits made on a detached HEAD are on no branch – with the worktree, its reflog and their last trace
    /// would go. Also when only git's data about it is left.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_detached_worktree_with_commits_of_its_own_stays(bool folderGone)
    {
        var detached = AddWorktree("detached", null, "--detach");
        GitSandbox.Commit(detached, "d.txt", "only here");
        var head = GitSandbox.Git(detached, "rev-parse", "HEAD").Trim();
        var repo = await AddMineAsync();
        if (folderGone)
        {
            Directory.Move(detached, detached + "-moved");
            await _board.RefreshAllAsync(Ct);
        }

        var question = await AskAsync(repo, "detached");
        Assert.Equal(WorktreeBlock.UnreferencedCommits, question.Block);
        Assert.False(await _board.RemoveWorktreeAsync(question with { Block = null }, Ct)); // checked again, whatever was asked

        Assert.Contains(head, GitSandbox.Git(_git.Mine, "worktree", "list", "--porcelain"), StringComparison.Ordinal);
    }

    /// <summary>git deletes ignored files with the worktree, unasked: only those the question showed (ADR 0010, review WP-04).</summary>
    [Fact]
    public async Task Only_the_ignored_files_the_question_named_are_deleted_with_the_worktree()
    {
        GitSandbox.Commit(_git.Mine, ".gitignore", "bin/\n.env\n*.local\n");
        var feature = AddWorktree("feature", null, "-b", "feature");
        Directory.CreateDirectory(Path.Combine(feature, "bin"));
        File.WriteAllText(Path.Combine(feature, "bin", "app.dll"), "x");
        File.WriteAllText(Path.Combine(feature, ".env"), "secret");
        var repo = await AddMineAsync();

        var question = await AskAsync(repo, "feature");
        Assert.True(question.CanRemove);
        Assert.Equal([".env", "bin/"], question.Ignored.Order(StringComparer.Ordinal));

        File.WriteAllText(Path.Combine(feature, "bin", "more.dll"), "x"); // in a folder already named: nothing new
        File.WriteAllText(Path.Combine(feature, "settings.local"), "a session wrote this meanwhile");
        Assert.False(await _board.RemoveWorktreeAsync(question, Ct));
        Assert.True(File.Exists(Path.Combine(feature, "settings.local")));
        Assert.Contains("ignorierte Dateien", Named(repo, "feature").Error, StringComparison.Ordinal);

        Assert.True(await _board.RemoveWorktreeAsync(await AskAsync(repo, "feature"), Ct));
        Assert.False(Directory.Exists(feature));
    }

    [Fact]
    public async Task Clean_up_suggests_the_worktrees_that_look_done()
    {
        // merged: pushed, and its commit is in origin/main
        var merged = AddWorktree("merged", null, "-b", "merged");
        GitSandbox.Commit(merged, "m.txt", "m");
        GitSandbox.Git(merged, "push", "-u", "origin", "merged");
        GitSandbox.Git(merged, "push", "origin", "merged:main");
        // gone: its upstream was deleted after the pull request (squash: not in main)
        var gone = AddWorktree("gone", null, "-b", "gone");
        GitSandbox.Commit(gone, "g.txt", "g");
        GitSandbox.Git(gone, "push", "-u", "origin", "gone");
        GitSandbox.Git(_git.Theirs, "push", "origin", "--delete", "gone");
        // detached on a commit main contains
        AddWorktree("detached", null, "--detach");
        // missing folder
        var missing = AddWorktree("missing", null, "-b", "missing");
        // not done: work of its own; a branch never pushed (a session that just started); the default branch's own
        // checkout behind origin/main (review WP-04)
        var own = AddWorktree("own", null, "-b", "own");
        GitSandbox.Commit(own, "o.txt", "o");
        AddWorktree("fresh", null, "-b", "fresh");
        AddFollowingWorktree("follow");
        MainMovesOn();
        // dirty and merged: shown, never removable
        File.WriteAllText(Path.Combine(merged, "work.txt"), "work");
        var repo = await AddMineAsync();
        Directory.Move(missing, missing + "-moved");
        await _board.RefreshAllAsync(Ct);

        var cleanup = await _board.FindCleanupAsync(repo, Ct);

        Assert.Equal("origin/main", cleanup.DefaultBranch);
        var found = cleanup.Candidates.ToDictionary(c => c.Worktree.Name);
        Assert.Equal(["detached", "gone", "merged", "missing"], found.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(WorktreeReason.Merged, found["merged"].Reason);
        Assert.Equal(WorktreeBlock.Changes, found["merged"].Block);
        Assert.Equal(WorktreeReason.UpstreamGone, found["gone"].Reason);
        Assert.Equal(WorktreeReason.DetachedMerged, found["detached"].Reason);
        Assert.Equal(WorktreeReason.FolderMissing, found["missing"].Reason);
        Assert.True(found["missing"].CanRemove);
        // Ticked: what is gone on the remote side; "merged" and "detached" only listed (review WP-04, decision of the user).
        Assert.Equal(["gone", "missing"], cleanup.Candidates.Where(c => c.Ticked).Select(c => c.Worktree.Name).Order(StringComparer.Ordinal));

        await _board.RemoveWorktreesAsync([.. cleanup.Candidates.Where(c => c.CanRemove)], Ct);

        Assert.Equal(["follow", "fresh", "merged", "own"], repo.Worktrees.Select(w => w.Name).Order(StringComparer.Ordinal));
        Assert.True(Directory.Exists(missing + "-moved")); // the moved folder itself is not touched
        Assert.Equal("work", File.ReadAllText(Path.Combine(merged, "work.txt")));
    }

    [Fact]
    public async Task A_fetch_in_the_repository_brings_its_worktrees_up_to_date()
    {
        AddFollowingWorktree("follow");
        var repo = await AddMineAsync();
        GitSandbox.Commit(_git.Theirs, "b.txt", "two");
        GitSandbox.Git(_git.Theirs, "push");

        await _board.FetchAsync(repo, Ct);

        Assert.Equal(1, Named(repo, "follow").Status!.Behind);
    }

    /// <summary>Review WP-04: the refs are shared – a pull in a worktree changes its repository's and the other worktrees' too.</summary>
    [Fact]
    public async Task A_pull_in_a_worktree_brings_its_repository_and_the_others_up_to_date()
    {
        AddFollowingWorktree("follow");
        AddFollowingWorktree("other");
        var repo = await AddMineAsync();
        GitSandbox.Commit(_git.Theirs, "b.txt", "two");
        GitSandbox.Git(_git.Theirs, "push");

        Assert.True(await _board.PullAsync(Named(repo, "follow"), Ct));

        Assert.Equal(0, Named(repo, "follow").Status!.Behind);
        Assert.Equal(1, repo.Status!.Behind);
        Assert.Equal(1, Named(repo, "other").Status!.Behind);
    }

    public void Dispose() => _git.Dispose();
}
