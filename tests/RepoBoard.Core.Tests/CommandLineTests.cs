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

    /// <summary>WP-10: the new version starts on the same data folder, with full paths, and shows its window.</summary>
    [Fact]
    public void A_restart_keeps_the_options_with_full_paths_and_drops_the_tray_start()
    {
        var restart = CommandLine.Restart(["--minimized", "--data-dir=scratch", "--lang=de", "--update-source=feed", "--EXIT", "--data-dir="]);

        Assert.Equal(
            ["--data-dir=" + Path.GetFullPath("scratch"), "--lang=de", "--update-source=" + Path.GetFullPath("feed"), "--data-dir="],
            restart);
    }
}
