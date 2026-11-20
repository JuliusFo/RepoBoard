using System.Globalization;
using RepoBoard.Core.Resources;

namespace RepoBoard.Core.Tests;

/// <summary>The localized texts of the Core (ADR 0005).</summary>
public sealed class ResourceTests
{
    public static TheoryData<string> TextClasses() => [.. ResourceChecks.TextClasses(typeof(TextFormat).Assembly).Select(t => t.Name)];

    [Theory]
    [MemberData(nameof(TextClasses))]
    public void Every_text_exists_in_English_and_German_with_the_same_placeholders(string textClass)
    {
        var type = ResourceChecks.TextClasses(typeof(TextFormat).Assembly).Single(t => t.Name == textClass);

        Assert.Empty(ResourceChecks.Problems(type));
    }

    /// <summary>Every English text with a placeholder says in its comment what the placeholder stands for (FerretSharp review 3.70.0, D11).</summary>
    [Fact]
    public void Every_text_with_a_placeholder_has_a_comment_for_the_translator() =>
        Assert.Empty(ResourceChecks.UncommentedPlaceholders("RepoBoard.Core"));

    [Fact]
    public void Plural_picks_the_form_by_count_and_the_texts_follow_the_UI_culture()
    {
        using (UiCulture.Use("en"))
        {
            Assert.Equal("1 commit", TextFormat.Plural(1, SyncText.CommitsOne, SyncText.CommitsOther));
        }

        Assert.Equal("3 Commits", TextFormat.Plural(3, SyncText.CommitsOne, SyncText.CommitsOther));
        Assert.Equal("1.234 Commits", TextFormat.Plural(CultureInfo.GetCultureInfo("de-DE"), 1234, "{0:N0} Commit", "{0:N0} Commits"));
    }
}
