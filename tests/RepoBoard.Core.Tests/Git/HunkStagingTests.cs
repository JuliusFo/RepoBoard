using RepoBoard.Core.Board;
using RepoBoard.Core.Git;
using RepoBoard.Core.Repos;
using RepoBoard.Testing;

namespace RepoBoard.Core.Tests.Git;

/// <summary>
/// Staging and unstaging hunks and lines (WP-25, ADR 0012) against real git: what ends up in the index, and that the
/// worktree is never touched.
/// </summary>
public sealed class HunkStagingTests : IDisposable
{
    private readonly GitSandbox _git = new();
    private readonly GitClient _client = new(new GitRunner());

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Lines(params string[] lines) => string.Concat(lines.Select(l => l + "\n"));

    private static readonly string Ten = Lines([.. Enumerable.Range(1, 10).Select(n => $"line {n}")]);

    private string Full(string path) => Path.Combine(_git.Mine, path);

    private string Index(string path) => GitSandbox.Git(_git.Mine, "show", $":{path}");

    private async Task<FileDiff> DiffAsync(string path, bool staged)
    {
        var (status, _) = await _client.StatusAsync(_git.Mine, Ct);
        var (diff, result) = await _client.DiffAsync(_git.Mine, status!.Changes.Single(c => c.Path == path), staged, Ct);
        Assert.True(diff is not null, result.Message);
        return diff;
    }

    /// <summary>The indexes of the given lines ("+text" or "-text") in the hunk.</summary>
    private static HashSet<int> Pick(DiffHunk hunk, params string[] lines) =>
        [.. lines.Select(l => hunk.Lines.ToList().FindIndex(x =>
            x.Kind == (l[0] == '+' ? DiffLineKind.Added : DiffLineKind.Removed) && x.Text == l[1..]))];

    /// <summary>Builds the patch from the current diff and applies it; the worktree file must stay byte for byte.</summary>
    private async Task<GitResult> ApplyAsync(string path, bool staged, int hunk, Func<DiffHunk, HashSet<int>?> lines)
    {
        var before = File.Exists(Full(path)) ? File.ReadAllBytes(Full(path)) : null;
        var diff = await DiffAsync(path, staged);
        var patch = HunkPatch.Build(path, diff, hunk, lines(diff.Hunks[hunk]), reverse: staged);
        Assert.Equal(HunkPatchKind.Patch, patch.Kind);

        var result = await _client.ApplyToIndexAsync(_git.Mine, patch, reverse: staged, Ct);

        Assert.Equal(before, File.Exists(Full(path)) ? File.ReadAllBytes(Full(path)) : null);
        return result;
    }

    private async Task StageAsync(string path, int hunk, Func<DiffHunk, HashSet<int>?> lines) =>
        Assert.True((await ApplyAsync(path, false, hunk, lines)).Success);

    private async Task UnstageAsync(string path, int hunk, Func<DiffHunk, HashSet<int>?> lines) =>
        Assert.True((await ApplyAsync(path, true, hunk, lines)).Success);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task One_hunk_goes_into_the_index_and_back_out(int hunk)
    {
        int[] changed = [0, 9, 19]; // lines 1, 10 and 20: three hunks
        string Changed(params int[] hunks) =>
            Lines([.. Enumerable.Range(0, 20).Select(i => hunks.Any(h => changed[h] == i) ? $"line {i + 1} changed" : $"line {i + 1}")]);
        GitSandbox.Commit(_git.Mine, "f.txt", Changed());
        File.WriteAllText(Full("f.txt"), Changed(0, 1, 2));
        Assert.Equal(3, (await DiffAsync("f.txt", staged: false)).Hunks.Count);

        await StageAsync("f.txt", hunk, _ => null);
        Assert.Equal(Changed(hunk), Index("f.txt"));

        GitSandbox.Git(_git.Mine, "add", "f.txt");
        await UnstageAsync("f.txt", hunk, _ => null);
        Assert.Equal(Changed([.. new[] { 0, 1, 2 }.Where(h => h != hunk)]), Index("f.txt"));
    }

    [Fact]
    public async Task Selected_lines_are_staged_and_unstaged_alone()
    {
        GitSandbox.Commit(_git.Mine, "f.txt", Lines("a", "b", "c", "d"));
        File.WriteAllText(Full("f.txt"), Lines("a", "B", "C", "d", "e"));

        // Removing "b" and adding "C": the "c" left out stays, the "B" and "e" left out do not come.
        await StageAsync("f.txt", 0, h => Pick(h, "-b", "+C"));
        Assert.Equal(Lines("a", "c", "C", "d"), Index("f.txt"));

        // Unstaging "-b": "b" is back, "C" (left out of this patch) stays staged.
        await UnstageAsync("f.txt", 0, h => Pick(h, "-b"));
        Assert.Equal(Lines("a", "b", "c", "C", "d"), Index("f.txt"));
    }

    [Fact]
    public async Task Part_of_a_new_file_creates_it_in_the_index_and_unstaging_takes_lines_out()
    {
        File.WriteAllText(Full("neu ä.txt"), Lines("x", "y", "z"));

        await StageAsync("neu ä.txt", 0, h => Pick(h, "+x", "+z"));
        Assert.Equal(Lines("x", "z"), Index("neu ä.txt"));

        await UnstageAsync("neu ä.txt", 0, h => Pick(h, "+z"));
        Assert.Equal(Lines("x"), Index("neu ä.txt"));
    }

    [Fact]
    public async Task Part_of_a_deletion_is_staged_and_part_of_a_staged_deletion_unstaged()
    {
        GitSandbox.Commit(_git.Mine, "gone.txt", Lines("a", "b", "c"));
        File.Delete(Full("gone.txt"));

        await StageAsync("gone.txt", 0, h => Pick(h, "-b"));
        Assert.Equal(Lines("a", "c"), Index("gone.txt"));

        GitSandbox.Git(_git.Mine, "rm", "--cached", "-q", "gone.txt");
        await UnstageAsync("gone.txt", 0, h => Pick(h, "-a", "-c"));
        Assert.Equal(Lines("a", "c"), Index("gone.txt"));
    }

    [Fact]
    public async Task A_staged_rename_is_unstaged_in_parts_under_its_new_name()
    {
        GitSandbox.Commit(_git.Mine, "long.txt", Ten);
        GitSandbox.Git(_git.Mine, "mv", "long.txt", "renamed.txt");
        File.AppendAllText(Full("renamed.txt"), Lines("line 11", "line 12"));
        GitSandbox.Git(_git.Mine, "add", "-A");

        await UnstageAsync("renamed.txt", 0, h => Pick(h, "+line 12"));

        Assert.Equal(Ten + Lines("line 11"), Index("renamed.txt"));
    }

    [Theory]
    // Old "a" without a newline, new "a\nb" without one: git shows "-a", "+a", "+b".
    [InlineData("+b", "a\nb")] // b follows a: a gets its newline
    [InlineData("-a +a", "a\n")] // only the newline
    public async Task A_last_line_without_newline_is_kept_right(string pick, string index)
    {
        GitSandbox.Commit(_git.Mine, "f.txt", "a");
        File.WriteAllText(Full("f.txt"), "a\nb");

        await StageAsync("f.txt", 0, h => Pick(h, pick.Split(' ')));

        Assert.Equal(index, Index("f.txt"));
    }

    [Fact]
    public async Task Windows_line_endings_stay_in_the_index()
    {
        GitSandbox.Commit(_git.Mine, "crlf.txt", "a\r\nb\r\nc\r\n");
        File.WriteAllText(Full("crlf.txt"), "a\r\nB\r\nc\r\nd\r\n");

        await StageAsync("crlf.txt", 0, h => Pick(h, "-b", "+B"));

        Assert.Equal("a\r\nB\r\nc\r\n", Index("crlf.txt"));
    }

    [Fact]
    public async Task A_patch_from_a_diff_that_is_no_longer_current_is_refused()
    {
        GitSandbox.Commit(_git.Mine, "f.txt", Lines("a", "b", "c"));
        File.WriteAllText(Full("f.txt"), Lines("a", "B", "c"));
        var diff = await DiffAsync("f.txt", staged: false);

        // Meanwhile the IDE stages another version.
        File.WriteAllText(Full("f.txt"), Lines("a", "X", "c"));
        GitSandbox.Git(_git.Mine, "add", "f.txt");
        var patch = HunkPatch.Build("f.txt", diff, 0, Pick(diff.Hunks[0], "+B"), reverse: false);

        Assert.False((await _client.ApplyToIndexAsync(_git.Mine, patch, reverse: false, Ct)).Success);
        Assert.Equal(Lines("a", "X", "c"), Index("f.txt"));
    }

    [Fact]
    public async Task The_users_whitespace_and_context_settings_do_not_refuse_a_patch()
    {
        GitSandbox.Git(_git.Mine, "config", "apply.whitespace", "error");
        GitSandbox.Git(_git.Mine, "config", "diff.context", "0");
        GitSandbox.Commit(_git.Mine, "f.txt", Ten);
        // Two hunks without context: a line with trailing spaces after line 2, line 9 changed.
        File.WriteAllText(Full("f.txt"), Ten.Replace("line 2\n", "line 2\ntrailing  \n", StringComparison.Ordinal)
            .Replace("line 9\n", "line 9 changed\n", StringComparison.Ordinal));

        await StageAsync("f.txt", 0, _ => null);

        Assert.Equal(Ten.Replace("line 2\n", "line 2\ntrailing  \n", StringComparison.Ordinal), Index("f.txt"));
    }

    [Fact]
    public void Every_change_of_the_file_is_the_whole_file_and_none_is_nothing()
    {
        var diff = FileDiff.Parse("@@ -1 +1 @@\n-a\n+b\n");

        Assert.Equal(HunkPatchKind.WholeFile, HunkPatch.Build("f.txt", diff, 0, null, reverse: false).Kind);
        Assert.Equal(HunkPatchKind.Nothing, HunkPatch.Build("f.txt", diff, 0, new HashSet<int>(), reverse: false).Kind);
    }

    [Fact]
    public void The_cut_off_hunk_of_a_truncated_diff_is_not_staged()
    {
        var diff = FileDiff.Parse("@@ -1,3 +1,3 @@\n-a\n-b\n-c\n+a\n+b\n+c\n", maxLines: 4);

        Assert.Equal(HunkPatchKind.Incomplete, HunkPatch.Build("f.txt", diff, 0, null, reverse: false).Kind);
    }

    [Fact]
    public void A_selection_that_would_leave_a_line_without_newline_inside_the_file_is_refused()
    {
        // Staged: "a" became "c", both without newline. Unstaging "-a" alone would put "a" before "c" without a newline.
        var diff = FileDiff.Parse("@@ -1 +1 @@\n-a\n\\ No newline at end of file\n+c\n\\ No newline at end of file\n");

        Assert.Equal(HunkPatchKind.SplitsLastLine, HunkPatch.Build("f.txt", diff, 0, new HashSet<int> { 0 }, reverse: true).Kind);
    }

    [Theory]
    [InlineData("a/plain name ä.txt", "a/plain name ä.txt")]
    [InlineData("a/quote\".txt", "\"a/quote\\\".txt\"")]
    [InlineData("a/tab\tä.txt", "\"a/tab\\t\\303\\244.txt\"")]
    [InlineData("a/trailing ", "\"a/trailing \"")]
    public void Paths_are_quoted_as_git_writes_them(string path, string quoted) =>
        Assert.Equal(quoted, HunkPatch.Quote(path));

    /// <summary>Through the board: a refused selection is the repository's error, the index stays.</summary>
    [Fact]
    public async Task A_file_that_is_not_utf8_is_not_staged_in_parts()
    {
        var original = Lines("a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l");
        GitSandbox.Commit(_git.Mine, "latin.txt", original);
        // Two hunks: "A" in the first line, a Latin-1 "ä" in the last.
        File.WriteAllBytes(Full("latin.txt"), [.. "A\nb\nc\nd\ne\nf\ng\nh\ni\nj\nk\n"u8, 0xE4, (byte)'\n']);
        var board = new BoardService(_client, new ConfigStore(Path.Combine(_git.Root, "config.json"), TimeProvider.System), TimeProvider.System);
        await board.AddAsync([_git.Mine], Ct);
        var repo = board.Repos.Single();
        var change = repo.Status!.Changes.Single();
        var (diff, _) = await board.DiffAsync(repo, change, staged: false, Ct);

        Assert.False(await board.StageHunkAsync(repo, change, diff!, staged: false, diff!.Hunks.Count - 1, null, Ct));
        Assert.Contains("UTF-8", repo.Error, StringComparison.Ordinal);
        Assert.Equal(2, diff.Hunks.Count);
        Assert.Equal(original, Index("latin.txt"));
    }

    private async Task<(BoardService Board, RepoState Repo)> BoardAsync()
    {
        var board = new BoardService(_client, new ConfigStore(Path.Combine(_git.Root, "config.json"), TimeProvider.System), TimeProvider.System);
        await board.AddAsync([_git.Mine], Ct);
        return (board, board.Repos.Single());
    }

    /// <summary>Through the board, as the diff view does it: the diff shown, then the click.</summary>
    private static async Task<(FileChange Change, FileDiff Diff)> ShownAsync(BoardService board, RepoState repo, string path, bool staged)
    {
        await board.RefreshAsync(repo, TestContext.Current.CancellationToken);
        var change = repo.Status!.Changes.Single(c => c.Path == path);
        var (diff, error) = await board.DiffAsync(repo, change, staged, TestContext.Current.CancellationToken);
        Assert.True(diff is not null, error);
        return (change, diff);
    }

    /// <summary>
    /// Review WP-25: a diff driver's text (textconv – git-crypt shows a secret in plain text) must never become the file in
    /// the index; parts are refused, the whole file is staged with its own content.
    /// </summary>
    [Fact]
    public async Task Parts_of_a_file_shown_through_textconv_are_not_staged()
    {
        File.WriteAllText(Full(".gitattributes"), "*.rev diff=rev\n");
        GitSandbox.Git(_git.Mine, "add", ".gitattributes");
        GitSandbox.Git(_git.Mine, "config", "diff.rev.textconv", "sed s/^/T:/");
        File.WriteAllText(Full("secret.rev"), Lines("abc", "def", "ghi"));
        var (board, repo) = await BoardAsync();
        var (change, diff) = await ShownAsync(board, repo, "secret.rev", staged: false);
        Assert.Equal("T:abc", diff.Hunks[0].Lines[0].Text);

        Assert.False(await board.StageHunkAsync(repo, change, diff, staged: false, 0, Pick(diff.Hunks[0], "+T:abc", "+T:ghi"), Ct));
        Assert.Contains("textconv", repo.Error, StringComparison.Ordinal);
        Assert.False(GitSandbox.TryGit(_git.Mine, "cat-file", "-e", ":secret.rev"));

        Assert.True(await board.StageHunkAsync(repo, change, diff, staged: false, 0, null, Ct));
        Assert.Equal(Lines("abc", "def", "ghi"), Index("secret.rev"));
    }

    /// <summary>Review WP-25: the file changed after its diff was shown – nothing is staged, not even as a whole file.</summary>
    [Fact]
    public async Task A_diff_that_is_no_longer_current_stages_nothing_through_the_board()
    {
        GitSandbox.Commit(_git.Mine, "f.txt", Lines("a", "b"));
        File.WriteAllText(Full("f.txt"), Lines("a", "B"));
        var (board, repo) = await BoardAsync();
        var (change, diff) = await ShownAsync(board, repo, "f.txt", staged: false);

        File.WriteAllText(Full("f.txt"), Lines("a", "B", "unseen"));

        Assert.False(await board.StageHunkAsync(repo, change, diff, staged: false, 0, null, Ct));
        Assert.Equal(Lines("a", "b"), Index("f.txt"));
    }

    /// <summary>
    /// Review WP-25: "Unstage hunk" on a staged rename takes out the content change and keeps the rename; the checkbox
    /// unstages the rename as a whole – both paths, not a staged deletion beside an untracked file.
    /// </summary>
    [Fact]
    public async Task A_staged_rename_keeps_its_rename_or_goes_back_as_a_whole()
    {
        GitSandbox.Commit(_git.Mine, "long.txt", Ten);
        GitSandbox.Git(_git.Mine, "mv", "long.txt", "renamed.txt");
        File.AppendAllText(Full("renamed.txt"), Lines("line 11"));
        GitSandbox.Git(_git.Mine, "add", "-A");
        var (board, repo) = await BoardAsync();
        var (change, diff) = await ShownAsync(board, repo, "renamed.txt", staged: true);

        Assert.True(await board.StageHunkAsync(repo, change, diff, staged: true, 0, null, Ct));
        Assert.Equal(Ten, Index("renamed.txt"));
        Assert.Contains("long.txt", repo.Status!.Changes.Single(c => c.Path == "renamed.txt").OriginalPath, StringComparison.Ordinal);

        Assert.True(await board.UnstageAsync(repo, ["renamed.txt"], Ct));
        Assert.Equal(Ten, Index("long.txt"));
        Assert.True(repo.Status!.Changes.Single(c => c.Path == "renamed.txt").Untracked);
        Assert.False(repo.Status!.Changes.Single(c => c.Path == "long.txt").IsStaged);
    }

    /// <summary>Review WP-25: with diff.context=0 a part of a hunk has context only on one side – git needs --unidiff-zero.</summary>
    [Fact]
    public async Task Part_of_a_hunk_without_context_is_staged()
    {
        GitSandbox.Git(_git.Mine, "config", "diff.context", "0");
        GitSandbox.Commit(_git.Mine, "f.txt", Ten);
        File.WriteAllText(Full("f.txt"), Ten.Replace("line 5\nline 6\n", "X\nY\n", StringComparison.Ordinal)
            .Replace("line 9\n", "line 9 changed\n", StringComparison.Ordinal));

        await StageAsync("f.txt", 0, h => Pick(h, "-line 5", "+X"));

        Assert.Equal(Ten.Replace("line 5\nline 6\n", "line 6\nX\n", StringComparison.Ordinal), Index("f.txt"));
    }

    /// <summary>A patch larger than a pipe's buffer: git's output is read while the patch is still written.</summary>
    [Fact]
    public async Task A_large_part_goes_through()
    {
        var old = Lines([.. Enumerable.Range(1, 2000).Select(n => $"line {n}")]);
        GitSandbox.Commit(_git.Mine, "big.txt", old);
        File.WriteAllText(Full("big.txt"), old.Replace("line ", "new line ", StringComparison.Ordinal));

        await StageAsync("big.txt", 0, h => [.. Enumerable.Range(0, h.Lines.Count - 1)]);

        Assert.Equal(1999, Index("big.txt").Split('\n').Count(l => l.StartsWith("new line ", StringComparison.Ordinal)));
    }

    /// <summary>Review WP-25: the line that gets its newline back in a CRLF file gets CR LF, not LF.</summary>
    [Fact]
    public async Task A_last_line_without_newline_gets_a_windows_one_in_a_crlf_file()
    {
        GitSandbox.Commit(_git.Mine, "f.txt", "a\r\nb");
        File.WriteAllText(Full("f.txt"), "a\r\nb\r\nc");

        await StageAsync("f.txt", 0, h => Pick(h, "+c"));

        Assert.Equal("a\r\nb\r\nc", Index("f.txt"));
    }

    /// <summary>Review WP-25: a link target or submodule commit is no text to stage in parts, also when a file became a link.</summary>
    [Theory]
    [InlineData("diff --git a/l b/l\nindex 1a2b..3c4d 120000\n--- a/l\n+++ b/l\n@@ -1 +1,2 @@\n-old\n+new\n+more\n")]
    [InlineData("diff --git a/s b/s\nindex 1a2b..3c4d 160000\n--- a/s\n+++ b/s\n@@ -1 +1,2 @@\n-Subproject commit 1\n+Subproject commit 2\n+x\n")]
    [InlineData("diff --git a/t b/t\ndeleted file mode 100644\nindex 1a2b..0000000\n--- a/t\n+++ /dev/null\n@@ -1 +0,0 @@\n-x\n"
        + "diff --git a/t b/t\nnew file mode 120000\nindex 0000000..3c4d\n--- /dev/null\n+++ b/t\n@@ -0,0 +1 @@\n+target\n")]
    public void Links_and_submodules_are_not_staged_in_parts(string output)
    {
        var diff = FileDiff.Parse(output);

        Assert.True(diff.IsSpecialFile);
        Assert.Equal(HunkPatchKind.SpecialFile, HunkPatch.Build("l", diff, 0, new HashSet<int> { 0 }, reverse: false).Kind);
    }

    /// <summary>Second review pass WP-25: a rename diff -M does not pair is two files – refused as such, not as a link.</summary>
    [Fact]
    public void A_deleted_and_an_added_file_in_one_diff_are_not_staged_in_parts()
    {
        var diff = FileDiff.Parse("diff --git a/old b/old\ndeleted file mode 100644\nindex 1a2b..0000000\n--- a/old\n+++ /dev/null\n@@ -1 +0,0 @@\n-x\n"
            + "diff --git a/new b/new\nnew file mode 100644\nindex 0000000..3c4d\n--- /dev/null\n+++ b/new\n@@ -0,0 +1 @@\n+y\n");

        Assert.False(diff.IsSpecialFile);
        Assert.Equal(HunkPatchKind.TwoFiles, HunkPatch.Build("new", diff, 0, new HashSet<int> { 0 }, reverse: true).Kind);
    }

    public void Dispose() => _git.Dispose();
}
