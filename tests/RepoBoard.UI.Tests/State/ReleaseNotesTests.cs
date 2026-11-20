using RepoBoard.UI.State;

namespace RepoBoard.UI.Tests.State;

/// <summary>WP-10 (FerretSharp WP-32): the release notes of an update are a changelog section, shown as plain blocks (no HTML from the internet).</summary>
public sealed class ReleaseNotesTests
{
    [Fact]
    public void A_changelog_section_becomes_headings_bullets_and_paragraphs()
    {
        var blocks = ReleaseNotes.Parse("""
            Installer and update notice (WP-10).

            ### Added
            - Install once with `Setup.exe`;
              RepoBoard reports new versions.
            - **Check now** in [Settings](https://example.com/settings).

            ---
            """);

        Assert.Collection(
            blocks,
            b => Assert.Equal((NoteBlockKind.Paragraph, "Installer and update notice (WP-10)."), (b.Kind, Text(b))),
            b => Assert.Equal((NoteBlockKind.Heading, "Added"), (b.Kind, Text(b))),
            b =>
            {
                Assert.Equal((NoteBlockKind.Bullet, "Install once with Setup.exe; RepoBoard reports new versions."), (b.Kind, Text(b)));
                Assert.Equal(new NoteSpan("Setup.exe", Code: true), b.Spans[1]);
            },
            b => Assert.Equal((NoteBlockKind.Bullet, "Check now in Settings."), (b.Kind, Text(b))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n ")]
    public void No_notes_are_no_blocks(string? notes) => Assert.Empty(ReleaseNotes.Parse(notes));

    [Fact]
    public void Unclosed_markers_stay_text()
    {
        var block = Assert.Single(ReleaseNotes.Parse("A ` and [a link without target"));

        Assert.Equal("A ` and [a link without target", Text(block));
        Assert.All(block.Spans, s => Assert.False(s.Code));
    }

    /// <summary>No image is loaded from the notes (they come from the internet): an image line shows its alt text.</summary>
    [Theory]
    [InlineData("https://raw.githubusercontent.com/JuliusFo/RepoBoard/v0.2.0/docs/a.png")]
    [InlineData("https://example.com/a.png")]
    [InlineData("docs/images/a.png")]
    public void An_image_shows_its_alt_text(string url)
    {
        var block = Assert.Single(ReleaseNotes.Parse($"  ![The alt text]({url})"));

        Assert.Equal((NoteBlockKind.Paragraph, "The alt text"), (block.Kind, Text(block)));
    }

    [Fact]
    public void An_image_without_alt_text_is_left_out() =>
        Assert.Empty(ReleaseNotes.Parse("![](https://example.com/a.png)"));

    [Fact]
    public void An_image_in_the_middle_of_text_stays_text()
    {
        var block = Assert.Single(ReleaseNotes.Parse("See ![the dialog](https://example.com/a.png) here."));

        Assert.Equal((NoteBlockKind.Paragraph, "See !the dialog here."), (block.Kind, Text(block)));
    }

    private static string Text(NoteBlock block) => string.Concat(block.Spans.Select(s => s.Text));
}
