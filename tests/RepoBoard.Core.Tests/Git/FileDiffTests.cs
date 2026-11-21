using RepoBoard.Core.Git;

namespace RepoBoard.Core.Tests.Git;

public sealed class FileDiffTests
{
    private static string Patch(params string[] lines) => string.Join('\n', lines) + "\n";

    [Fact]
    public void Hunks_carry_old_and_new_line_numbers()
    {
        var diff = FileDiff.Parse(Patch(
            "diff --git a/a.cs b/a.cs",
            "index 1111111..2222222 100644",
            "--- a/a.cs",
            "+++ b/a.cs",
            "@@ -3,4 +3,4 @@ class A",
            " keep",
            "-old line",
            "+new line",
            " keep too",
            " last",
            "@@ -20 +20,2 @@",
            " x",
            "+y"));

        Assert.False(diff.IsBinary);
        Assert.Equal(2, diff.Hunks.Count);
        var first = diff.Hunks[0];
        Assert.Equal((3, 4, 3, 4, "class A"), (first.OldStart, first.OldCount, first.NewStart, first.NewCount, first.Section));
        Assert.Equal(
            [(DiffLineKind.Context, 3, 3), (DiffLineKind.Removed, 4, null), (DiffLineKind.Added, null, 4), (DiffLineKind.Context, 5, 5), (DiffLineKind.Context, 6, 6)],
            first.Lines.Select(l => (l.Kind, l.OldNumber, l.NewNumber)));
        Assert.Equal(["x", "y"], diff.Hunks[1].Lines.Select(l => l.Text));
        Assert.Equal((1, 2), (diff.Hunks[1].OldCount, diff.Hunks[1].NewCount));
    }

    [Fact]
    public void An_empty_context_line_counts_and_the_missing_newline_marks_its_line()
    {
        // diff.suppressBlankEmpty writes an empty context line without its leading space.
        var diff = FileDiff.Parse(Patch(
            "@@ -1,3 +1,3 @@",
            " a",
            "",
            "-b",
            "\\ No newline at end of file",
            "+b\r",
            "\\ No newline at end of file"));

        var lines = diff.Hunks.Single().Lines;
        Assert.Equal([DiffLineKind.Context, DiffLineKind.Context, DiffLineKind.Removed, DiffLineKind.Added], lines.Select(l => l.Kind));
        Assert.Equal("", lines[1].Text);
        Assert.True(lines[2].NoNewlineAtEnd);
        Assert.True(lines[3] is { NoNewlineAtEnd: true, EndsWithCr: true, Text: "b" });
        Assert.False(lines[2].EndsWithCr);
    }

    [Fact]
    public void Paired_lines_mark_only_the_changed_words()
    {
        var diff = FileDiff.Parse(Patch(
            "@@ -1,3 +1,2 @@",
            "-var count = 1;",
            "-gone",
            "+var total = 1;"));

        var lines = diff.Hunks.Single().Lines;
        Assert.Equal([new DiffSpan(4, 5)], lines[0].Changed);
        Assert.Empty(lines[1].Changed);
        Assert.Equal([new DiffSpan(4, 5)], lines[2].Changed);
    }

    [Theory]
    [InlineData("Binary files a/x.png and b/x.png differ")]
    [InlineData("GIT binary patch")]
    public void Binary_files_have_no_hunks(string marker)
    {
        var diff = FileDiff.Parse(Patch("diff --git a/x.png b/x.png", marker));

        Assert.True(diff.IsBinary);
        Assert.Empty(diff.Hunks);
        Assert.False(diff.HasNoTextChanges);
    }

    [Fact]
    public void A_mode_change_alone_has_no_text_changes() =>
        Assert.True(FileDiff.Parse(Patch("diff --git a/s.sh b/s.sh", "old mode 100644", "new mode 100755")).HasNoTextChanges);

    [Fact]
    public void Lines_above_the_limit_are_cut()
    {
        var diff = FileDiff.Parse(Patch("@@ -0,0 +1,5 @@", "+1", "+2", "+3", "+4", "+5"), maxLines: 3);

        Assert.True(diff.IsTruncated);
        Assert.Equal(["1", "2", "3"], diff.Hunks.Single().Lines.Select(l => l.Text));
    }

    /// <summary>Review WP-05: a file of many long changed lines stops comparing words once its budget is spent.</summary>
    [Fact]
    public void Word_highlighting_stops_at_the_budget_of_the_file()
    {
        // 250 words and 249 spaces: 499 tokens, 249,001 token pairs per line pair – eight of them fit in the budget.
        static string Line(string last) => string.Join(' ', Enumerable.Range(0, 249).Select(n => $"w{n}")) + " " + last;
        var pairs = 9;
        var lines = new List<string> { $"@@ -1,{pairs} +1,{pairs} @@" };
        lines.AddRange(Enumerable.Repeat("-" + Line("old"), pairs));
        lines.AddRange(Enumerable.Repeat("+" + Line("new"), pairs));

        var added = FileDiff.Parse(Patch([.. lines])).Hunks.Single().Lines.Where(l => l.Kind == DiffLineKind.Added).ToList();

        Assert.All(added.Take(8), l => Assert.NotEmpty(l.Changed));
        Assert.Empty(added[8].Changed);
    }

    [Theory]
    [InlineData("return a + b;", "return a - b;", "+", "-")]
    [InlineData("foo(bar)", "foo(bar, baz)", "", ", baz")]
    [InlineData("same", "same", "", "")]
    [InlineData("all old", "fresh", "", "")]
    public void Words_differing_between_two_lines(string oldText, string newText, string oldChanged, string newChanged)
    {
        var (old, @new) = DiffWords.Compare(oldText, newText);

        Assert.Equal(oldChanged, string.Concat(old.Select(s => oldText.Substring(s.Start, s.Length))));
        Assert.Equal(newChanged, string.Concat(@new.Select(s => newText.Substring(s.Start, s.Length))));
    }
}
