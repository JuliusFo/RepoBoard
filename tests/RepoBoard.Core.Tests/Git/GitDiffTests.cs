using RepoBoard.Core.Board;
using RepoBoard.Core.Git;
using RepoBoard.Core.Repos;
using RepoBoard.Testing;

namespace RepoBoard.Core.Tests.Git;

/// <summary>The diff of a changed file from real git, for each kind of change the opened row lists.</summary>
public sealed class GitDiffTests : IDisposable
{
    private readonly GitSandbox _git = new();
    private readonly GitClient _client = new(new GitRunner());

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<FileChange> ChangeAsync(string path)
    {
        var (status, _) = await _client.StatusAsync(_git.Mine, Ct);
        return status!.Changes.Single(c => c.Path == path);
    }

    private async Task<FileDiff> DiffAsync(string path, bool staged)
    {
        var (diff, result) = await _client.DiffAsync(_git.Mine, await ChangeAsync(path), staged, Ct);
        Assert.True(diff is not null, result.Message);
        return diff;
    }

    private static IEnumerable<string> Changed(FileDiff diff) =>
        diff.Hunks.SelectMany(h => h.Lines).Where(l => l.Kind != DiffLineKind.Context).Select(l => $"{(l.Kind == DiffLineKind.Added ? '+' : '-')}{l.Text}");

    [Fact]
    public async Task Unstaged_and_staged_changes_of_one_file_are_diffed_apart()
    {
        File.WriteAllText(Path.Combine(_git.Mine, "a.txt"), "staged\n");
        GitSandbox.Git(_git.Mine, "add", "a.txt");
        File.WriteAllText(Path.Combine(_git.Mine, "a.txt"), "worktree\n");

        Assert.Equal(["-one", "+staged"], Changed(await DiffAsync("a.txt", staged: true)));
        Assert.Equal(["-staged", "+worktree"], Changed(await DiffAsync("a.txt", staged: false)));
    }

    [Fact]
    public async Task A_new_file_is_all_added_and_a_binary_one_is_marked()
    {
        File.WriteAllText(Path.Combine(_git.Mine, "neu ä.txt"), "x\ny\n");
        File.WriteAllBytes(Path.Combine(_git.Mine, "b.bin"), [0, 1, 2, 0]);

        var text = await DiffAsync("neu ä.txt", staged: false);
        Assert.Equal(["+x", "+y"], Changed(text));
        Assert.Equal([1, 2], text.Hunks.Single().Lines.Select(l => l.NewNumber ?? 0));
        Assert.True((await DiffAsync("b.bin", staged: false)).IsBinary);
    }

    /// <summary>E2E WP-05: with core.autocrlf git warns on stderr and ends with 1 – still the diff, not a failure.</summary>
    [Fact]
    public async Task A_warning_of_git_does_not_hide_the_diff_of_a_new_file()
    {
        GitSandbox.Git(_git.Mine, "config", "core.autocrlf", "true");
        File.WriteAllText(Path.Combine(_git.Mine, "lf.txt"), "x\n");

        Assert.Equal(["+x"], Changed(await DiffAsync("lf.txt", staged: false)));
    }

    [Fact]
    public async Task A_new_file_above_the_limit_is_not_read()
    {
        var path = Path.Combine(_git.Mine, "big.log");
        using (var file = File.Create(path))
        {
            file.SetLength(FileDiff.MaxNewFileBytes + 1);
        }

        Assert.True((await DiffAsync("big.log", staged: false)).IsTooLarge);
    }

    [Fact]
    public async Task A_staged_rename_shows_what_changed_besides_the_name()
    {
        // Long enough that one more line keeps it similar enough for git's rename detection.
        GitSandbox.Commit(_git.Mine, "long.txt", string.Concat(Enumerable.Range(1, 10).Select(n => $"line {n}\n")));
        GitSandbox.Git(_git.Mine, "mv", "long.txt", "renamed.txt");
        File.AppendAllText(Path.Combine(_git.Mine, "renamed.txt"), "line 11\n");
        GitSandbox.Git(_git.Mine, "add", "-A");

        var change = await ChangeAsync("renamed.txt");
        Assert.Equal("long.txt", change.OriginalPath);
        Assert.Equal(["+line 11"], Changed(await DiffAsync("renamed.txt", staged: true)));
    }

    [Fact]
    public async Task Staged_files_before_the_first_commit_are_diffed_against_nothing()
    {
        var fresh = Path.Combine(_git.Root, "fresh");
        Directory.CreateDirectory(fresh);
        GitSandbox.Git(fresh, "init", "-b", "main");
        File.WriteAllText(Path.Combine(fresh, "first.txt"), "hello\n");
        GitSandbox.Git(fresh, "add", "first.txt");

        var (status, _) = await _client.StatusAsync(fresh, Ct);
        var (diff, _) = await _client.DiffAsync(fresh, status!.Changes.Single(), staged: true, Ct);

        Assert.Equal(["+hello"], Changed(diff!));
    }

    [Fact]
    public async Task A_failing_diff_is_reported_in_place_not_as_the_repository_error()
    {
        var board = new BoardService(_client, new ConfigStore(Path.Combine(_git.Root, "config.json"), TimeProvider.System), TimeProvider.System);
        await board.AddAsync([_git.Mine], Ct);
        var repo = board.Repos.Single();
        var outside = new FileChange("../outside.txt", null, 'M', '.', false, false);

        var (diff, error) = await board.DiffAsync(repo, outside, staged: true, Ct);

        Assert.Null(diff);
        Assert.StartsWith("Diff", error, StringComparison.Ordinal);
        Assert.Contains("outside", error, StringComparison.Ordinal);
        Assert.Null(repo.Error);
    }

    public void Dispose() => _git.Dispose();
}
