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

    private static RepoState Named(RepoState repo, string name) => repo.Worktrees.Single(w => w.Name == name);

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
    public void The_porcelain_list_is_read_field_by_field(string output, string? branch, string? locked, string? prunable, bool bare)
    {
        var worktree = Assert.Single(WorktreeInfo.ParseList(output));

        Assert.Equal(Path.GetFullPath("/r"), worktree.Path);
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

    [Fact]
    public async Task Adding_a_worktree_puts_its_repository_on_the_board()
    {
        var feature = AddWorktree("feature", null, "-b", "feature");

        await _board.AddAsync([feature], Ct);

        var repo = Assert.Single(_board.Repos);
        Assert.Equal("mine", repo.Name);
        Assert.Equal("feature", Assert.Single(repo.Worktrees).Name);
    }

    [Fact]
    public async Task The_scan_finds_the_repository_but_not_its_worktree_beside_it()
    {
        AddWorktree("feature", null, "-b", "feature");
        GitSandbox.Git(_git.Mine, "-c", "protocol.file.allow=always", "submodule", "add", _git.Remote, "sub"); // a .git file too, but no worktree

        var found = RepoScanner.Find(_git.Root).Select(Path.GetFileName).Order().ToList();

        Assert.Equal(["mine", "theirs"], found);
        Assert.False(RepoScanner.IsLinkedWorktree(Path.Combine(_git.Mine, "sub", ".git")));
        Assert.True(RepoScanner.IsLinkedWorktree(Path.Combine(_git.Root, "feature", ".git")));
    }

    [Fact]
    public async Task Removing_a_clean_worktree_keeps_its_branch()
    {
        var feature = AddWorktree("feature", null, "-b", "feature");
        var repo = await AddMineAsync();

        Assert.True(await _board.RemoveWorktreeAsync(Named(repo, "feature"), Ct));

        Assert.False(Directory.Exists(feature));
        Assert.Empty(repo.Worktrees);
        Assert.Contains("refs/heads/feature", GitSandbox.Git(_git.Mine, "for-each-ref", "--format=%(refname)", "refs/heads"), StringComparison.Ordinal);
        Assert.Contains(_board.Activity.Entries, e => e.Text.EndsWith(" entfernt; Branch feature bleibt.", StringComparison.Ordinal));
    }

    /// <summary>ADR 0010: a worktree with work in it is never removed – not even if it changed after the question.</summary>
    [Theory]
    [InlineData("new")]
    [InlineData("modified")]
    [InlineData("staged")]
    public async Task A_worktree_with_changes_stays(string change)
    {
        var feature = AddWorktree("feature", null, "-b", "feature");
        var repo = await AddMineAsync();
        var file = Path.Combine(feature, change == "new" ? "new.txt" : "a.txt");
        File.WriteAllText(file, "work");
        if (change == "staged")
        {
            GitSandbox.Git(feature, "add", "-A");
        }

        var worktree = Named(repo, "feature");
        Assert.Equal(WorktreeBlock.Changes, (await _board.InspectWorktreeAsync(worktree, Ct)).Block);
        Assert.False(await _board.RemoveWorktreeAsync(worktree, Ct));

        Assert.Equal("work", File.ReadAllText(file));
        Assert.Same(worktree, Assert.Single(repo.Worktrees));
        Assert.Contains("geänderte oder neue Dateien", worktree.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_locked_worktree_stays()
    {
        var feature = AddWorktree("feature", null, "-b", "feature");
        GitSandbox.Git(_git.Mine, "worktree", "lock", "--reason", "usb stick", feature);
        var repo = await AddMineAsync();

        var worktree = Named(repo, "feature");
        Assert.Equal("usb stick", worktree.Worktree!.Locked);
        Assert.Equal(WorktreeBlock.Locked, (await _board.InspectWorktreeAsync(worktree, Ct)).Block);
        Assert.False(await _board.RemoveWorktreeAsync(worktree, Ct));
        Assert.True(Directory.Exists(feature));
    }

    /// <summary>git deletes ignored files with the worktree, unasked: the question names them (ADR 0010).</summary>
    [Fact]
    public async Task The_question_names_the_ignored_files_git_deletes_with_the_worktree()
    {
        GitSandbox.Commit(_git.Mine, ".gitignore", "bin/\n.env\n");
        var feature = AddWorktree("feature", null, "-b", "feature");
        Directory.CreateDirectory(Path.Combine(feature, "bin"));
        File.WriteAllText(Path.Combine(feature, "bin", "app.dll"), "x");
        File.WriteAllText(Path.Combine(feature, ".env"), "secret");
        var repo = await AddMineAsync();

        var candidate = await _board.InspectWorktreeAsync(Named(repo, "feature"), Ct);

        Assert.True(candidate.CanRemove);
        Assert.Equal([".env", "bin/"], candidate.Ignored.Order(StringComparer.Ordinal));
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
        // not done: work of its own; a branch never pushed (a session that just started)
        var own = AddWorktree("own", null, "-b", "own");
        GitSandbox.Commit(own, "o.txt", "o");
        AddWorktree("fresh", null, "-b", "fresh");
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

        await _board.RemoveWorktreesAsync([.. cleanup.Candidates.Where(c => c.CanRemove).Select(c => c.Worktree)], Ct);

        Assert.Equal(["fresh", "merged", "own"], repo.Worktrees.Select(w => w.Name).Order(StringComparer.Ordinal));
        Assert.True(Directory.Exists(missing + "-moved")); // the moved folder itself is not touched
        Assert.Equal("work", File.ReadAllText(Path.Combine(merged, "work.txt")));
    }

    [Fact]
    public async Task A_fetch_in_the_repository_brings_its_worktrees_up_to_date()
    {
        GitSandbox.Git(_git.Mine, "worktree", "add", "--track", "-b", "follow", Path.Combine(_git.Root, "follow"), "origin/main");
        var repo = await AddMineAsync();
        GitSandbox.Commit(_git.Theirs, "b.txt", "two");
        GitSandbox.Git(_git.Theirs, "push");

        await _board.FetchAsync(repo, Ct);

        Assert.Equal(1, Named(repo, "follow").Status!.Behind);
    }

    public void Dispose() => _git.Dispose();
}
