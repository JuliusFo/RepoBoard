using System.Globalization;

namespace RepoBoard.Core.Tests;

public sealed class CommandLineTests
{
    [Theory]
    [InlineData("de-DE", new string[0], "de")]
    [InlineData("en-US", new string[0], "en")]
    [InlineData("fr-FR", new string[0], "en")] // neither language: English
    [InlineData("de-DE", new[] { "--lang=en" }, "en")]
    [InlineData("en-US", new[] { "--LANG=DE" }, "de")]
    public void The_UI_language_follows_lang_then_Windows(string windows, string[] args, string expected) =>
        Assert.Equal(expected, CommandLine.UiCulture(args, CultureInfo.GetCultureInfo(windows)).Name);

    [Theory]
    [InlineData(new[] { "--minimized" }, true)]
    [InlineData(new[] { "--lang=de", "--MINIMIZED" }, true)]
    [InlineData(new[] { "--minimized=no" }, false)] // a switch, not an option with a value
    [InlineData(new string[0], false)]
    public void A_switch_is_there_or_not(string[] args, bool expected) =>
        Assert.Equal(expected, CommandLine.Has(args, CommandLine.Minimized));

    [Fact]
    public void An_option_takes_its_first_value() =>
        Assert.Equal(@"C:\a", CommandLine.ValueOf([@"--data-dir=C:\a", @"--data-dir=C:\b"], CommandLine.DataDir));
}
