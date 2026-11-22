using RepoBoard.UI.State;

namespace RepoBoard.UI.Tests.State;

/// <summary>Direction controls in a diff or commit text are shown, not obeyed (review 0.4.0, A24).</summary>
public sealed class VisibleTextTests
{
    [Fact]
    public void Direction_controls_are_shown_as_code_points()
    {
        Assert.Equal("if (role != \"admin⟨U+202E⟩ ⟨U+2066⟩// is user⟨U+2069⟩ ⟨U+2066⟩\")",
            VisibleText.Of("if (role != \"admin‮ ⁦// is user⁩ ⁦\")"));
        Assert.Equal("a⟨U+200F⟩b⟨U+061C⟩c", VisibleText.Of("a‏b؜c"));
    }

    [Theory]
    [InlineData("plain code();")]
    [InlineData("Umlaute äöü und مرحبا")] // right-to-left letters themselves are text, not controls
    public void Other_text_stays_as_it_is(string text) => Assert.Same(text, VisibleText.Of(text));
}
