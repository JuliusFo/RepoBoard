using Microsoft.Extensions.Time.Testing;
using RepoBoard.Core.Board;
using RepoBoard.Core.Git;
using RepoBoard.Core.IO;
using RepoBoard.Core.Repos;
using RepoBoard.Core.Resources;

namespace RepoBoard.Core.Tests.Board;

/// <summary>Discarding changes and the stash (WP-06, ADR 0016) against real git.</summary>
public sealed class StashTests : IDisposable
{
    private readonly GitSandbox _git = new();
    private readonly GitClient _client = new(new GitRunner());
    private readonly FakeTimeProvider _time = new();
    private readonly FakeRecycleBin _bin;
    private readonly BoardService _board;

    public StashTests()
    {
        _bin = new FakeRecycleBin(Path.Combine(_git.Root, "recycled"));
        _board = NewBoard(_bin);
    }

    private BoardService NewBoard(IRecycleBin? bin) =>
        new(_client, new ConfigStore(Path.Combine(_git.Root, "config.json"), _time), _time, recycleBin: bin);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Mine(string path) => Path.Combine(_git.Mine, path);

    private string Read(string path) => File.ReadAllText(Mine(path));

    private IReadOnlyList<string> StashList() =>
        GitSandbox.Git(_git.Mine, "stash", "list", "--format=%gs").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static async Task<RepoState> OpenAsync(BoardService board, string path, CancellationToken ct)
    {
        await board.AddAsync([path], ct);
        var repo = board.Repos.Single();
        await board.SetWantsDetailsAsync(repo, true, ct);
        return repo;
    }

    private Task<RepoState> OpenMineAsync() => OpenAsync(_board, _git.Mine, Ct);

    [Fact]
    public async Task Discard_puts_files_back_keeps_the_changes_in_the_stash_and_apply_brings_them_back()
    {
        GitSandbox.Commit(_git.Mine, "b.txt", "b");
        GitSandbox.Commit(_git.Mine, "d.txt", "d");
        GitSandbox.Commit(_git.Mine, "[e].txt", "brackets");
        GitSandbox.Commit(_git.Mine, "e.txt", "e");
        File.WriteAllText(Mine("b.txt"), "b changed");
        File.Delete(Mine("d.txt"));
        File.WriteAllText(Mine("[e].txt"), "brackets changed");
        File.WriteAllText(Mine("e.txt"), "e stays changed"); // "[e].txt" as a pattern would match it (review WP-05)
        File.WriteAllText(Mine("c.txt"), "new");
        var repo = await OpenMineAsync();

        Assert.True(await _board.DiscardChangesAsync(repo, ["b.txt", "d.txt", "[e].txt", "c.txt"], Ct), repo.Error);

        Assert.Equal("b", Read("b.txt"));
        Assert.Equal("d", Read("d.txt"));
        Assert.Equal("brackets", Read("[e].txt"));
        Assert.Equal("e stays changed", Read("e.txt"));
        Assert.False(File.Exists(Mine("c.txt")));
        Assert.Equal([Path.GetFullPath(Mine("c.txt"))], _bin.Recycled);
        var message = Assert.Single(repo.Details!.Stashes).Message;
        Assert.EndsWith(TextFormat.Format(SyncText.Discard_StashMessage, "b.txt, d.txt, [e].txt"), message, StringComparison.Ordinal);
        Assert.Equal(["[e].txt", "b.txt", "d.txt"], await _board.StashFilesAsync(repo, repo.Details.Stashes[0], Ct));
        Assert.Contains(_board.Activity.Entries, e => e.Text == TextFormat.Format(SyncText.Discarded_Both, TextFormat.Format(SyncText.Discard_StashMessage, "b.txt, d.txt, [e].txt")));

        Assert.True(await _board.ApplyStashAsync(repo, repo.Details.Stashes[0], pop: false, Ct), repo.Error);

        Assert.Equal("b changed", Read("b.txt"));
        Assert.False(File.Exists(Mine("d.txt")));
        Assert.Equal("brackets changed", Read("[e].txt"));
        Assert.Single(repo.Details!.Stashes); // apply keeps the entry
    }

    [Fact]
    public async Task A_partly_staged_file_goes_back_to_its_staged_version()
    {
        File.WriteAllText(Mine("a.txt"), "staged\n");
        GitSandbox.Git(_git.Mine, "add", "a.txt");
        File.WriteAllText(Mine("a.txt"), "staged\nmore\n");
        var repo = await OpenMineAsync();

        Assert.True(await _board.DiscardChangesAsync(repo, ["a.txt"], Ct), repo.Error);

        Assert.Equal("staged\n", Read("a.txt"));
        Assert.Equal("a.txt", GitSandbox.Git(_git.Mine, "diff", "--cached", "--name-only").Trim()); // the index stays
        Assert.Equal(["a.txt"], await _board.StashFilesAsync(repo, Assert.Single(repo.Details!.Stashes), Ct));
    }

    [Fact]
    public async Task Discard_refuses_when_a_file_changed_since_the_question()
    {
        File.WriteAllText(Mine("a.txt"), "changed");
        File.WriteAllText(Mine("c.txt"), "new");
        var repo = await OpenMineAsync();
        GitSandbox.Git(_git.Mine, "add", "c.txt"); // another session staged it meanwhile

        Assert.False(await _board.DiscardChangesAsync(repo, ["a.txt", "c.txt"], Ct));

        Assert.Contains(SyncText.Operation_FilesChanged, repo.Error, StringComparison.Ordinal);
        Assert.Equal("changed", Read("a.txt"));
        Assert.True(File.Exists(Mine("c.txt")));
        Assert.Empty(StashList());
        Assert.Empty(_bin.Recycled);
    }

    [Fact]
    public async Task Without_a_recycle_bin_nothing_is_discarded()
    {
        var board = NewBoard(null);
        File.WriteAllText(Mine("a.txt"), "changed");
        File.WriteAllText(Mine("c.txt"), "new");
        var repo = await OpenAsync(board, _git.Mine, Ct);

        Assert.False(await board.DiscardChangesAsync(repo, ["a.txt", "c.txt"], Ct));

        Assert.Contains(SyncText.Discard_NoRecycleBin, repo.Error, StringComparison.Ordinal);
        Assert.Equal("changed", Read("a.txt")); // not stashed first: all or nothing
        Assert.True(File.Exists(Mine("c.txt")));
        Assert.Empty(StashList());
    }

    [Fact]
    public async Task Stash_with_new_files_and_pop_brings_everything_back()
    {
        File.WriteAllText(Mine("a.txt"), "changed");
        File.WriteAllText(Mine("c.txt"), "new");
        var repo = await OpenMineAsync();

        Assert.True(await _board.StashAsync(repo, "  wip  ", includeUntracked: true, Ct), repo.Error);

        Assert.True(repo.Status!.IsClean);
        var entry = Assert.Single(repo.Details!.Stashes);
        Assert.Equal("On main: wip", entry.Message);
        Assert.Equal(["a.txt", "c.txt"], await _board.StashFilesAsync(repo, entry, Ct));

        Assert.True(await _board.ApplyStashAsync(repo, entry, pop: true, Ct), repo.Error);

        Assert.Equal("changed", Read("a.txt"));
        Assert.Equal("new", Read("c.txt"));
        Assert.Empty(repo.Details!.Stashes);
    }

    [Fact]
    public async Task Stash_without_new_files_says_so_instead_of_saving_nothing()
    {
        File.WriteAllText(Mine("c.txt"), "new");
        var repo = await OpenMineAsync();

        // git itself would succeed with "No local changes to save" and save nothing.
        Assert.False(await _board.StashAsync(repo, "", includeUntracked: false, Ct));

        Assert.Contains(SyncText.Stash_OnlyNew, repo.Error, StringComparison.Ordinal);
        Assert.Empty(StashList());
    }

    [Fact]
    public async Task Drop_takes_the_entry_the_page_showed_even_after_the_list_moved()
    {
        File.WriteAllText(Mine("a.txt"), "first");
        var repo = await OpenMineAsync();
        Assert.True(await _board.StashAsync(repo, "first", includeUntracked: false, Ct), repo.Error);
        var shown = repo.Details!.Stashes[0];
        File.WriteAllText(Mine("a.txt"), "second");
        GitSandbox.Git(_git.Mine, "stash", "push", "-m", "second"); // another session: "first" is stash@{1} now

        Assert.True(await _board.DropStashAsync(repo, shown, Ct), repo.Error);

        Assert.Equal(["On main: second"], StashList());
        Assert.False(await _board.DropStashAsync(repo, shown, Ct));
        Assert.Contains(SyncText.Stash_Gone, repo.Error, StringComparison.Ordinal);
        Assert.Equal(["On main: second"], StashList());
    }

    [Fact]
    public async Task Pop_with_a_conflict_keeps_the_entry()
    {
        File.WriteAllText(Mine("a.txt"), "stashed");
        var repo = await OpenMineAsync();
        Assert.True(await _board.StashAsync(repo, "mine", includeUntracked: false, Ct), repo.Error);
        GitSandbox.Commit(_git.Mine, "a.txt", "committed");
        await _board.RefreshAsync(repo, Ct);
        var entry = repo.Details!.Stashes[0];

        Assert.False(await _board.ApplyStashAsync(repo, entry, pop: true, Ct));

        Assert.Contains(SyncText.Stash_ApplyConflicts, repo.Error, StringComparison.Ordinal);
        Assert.Equal(1, repo.Status!.Conflicts);
        Assert.Single(repo.Details!.Stashes);
        // With the conflict open, applying again keeps out with a reason.
        Assert.False(await _board.ApplyStashAsync(repo, entry, pop: false, Ct));
        Assert.Contains(SyncText.Stash_Conflicts, repo.Error, StringComparison.Ordinal);
    }

    public void Dispose() => _git.Dispose();

    /// <summary>Moves the files into a folder of the test, as the system's recycle bin would keep them.</summary>
    private sealed class FakeRecycleBin(string folder) : IRecycleBin
    {
        public List<string> Recycled { get; } = [];

        public Task<string?> RecycleAsync(IReadOnlyList<string> files, CancellationToken ct)
        {
            Directory.CreateDirectory(folder);
            foreach (var file in files)
            {
                File.Move(file, Path.Combine(folder, Recycled.Count + "-" + Path.GetFileName(file)));
                Recycled.Add(file);
            }

            return Task.FromResult<string?>(null);
        }
    }
}
