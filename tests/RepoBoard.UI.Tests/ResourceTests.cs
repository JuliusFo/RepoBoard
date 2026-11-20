using RepoBoard.UI.Resources;

namespace RepoBoard.UI.Tests;

/// <summary>The localized texts of the UI (ADR 0005).</summary>
public sealed class ResourceTests
{
    public static TheoryData<string> TextClasses() => [.. ResourceChecks.TextClasses(typeof(BoardText).Assembly).Select(t => t.Name)];

    [Theory]
    [MemberData(nameof(TextClasses))]
    public void Every_text_exists_in_English_and_German_with_the_same_placeholders(string textClass)
    {
        var type = ResourceChecks.TextClasses(typeof(BoardText).Assembly).Single(t => t.Name == textClass);

        Assert.Empty(ResourceChecks.Problems(type));
    }

    [Fact]
    public void Every_text_with_a_placeholder_has_a_comment_for_the_translator() =>
        Assert.Empty(ResourceChecks.UncommentedPlaceholders("RepoBoard.UI"));
}
