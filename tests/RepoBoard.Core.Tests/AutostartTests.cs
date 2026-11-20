namespace RepoBoard.Core.Tests;

/// <summary>The start-with-Windows entry (ADR 0006): what it runs, and that a test instance never takes the user's entry.</summary>
public sealed class AutostartTests
{
    private static readonly string Exe = Path.Combine(Path.GetTempPath(), "Program Files", "RepoBoard", "RepoBoard.exe");

    [Fact]
    public void The_users_own_folder_starts_in_the_notification_area_under_the_app_name()
    {
        Assert.Equal("RepoBoard", Autostart.ValueName(AppPaths.Default));
        Assert.Equal($"\"{Exe}\" --minimized", Autostart.Command(Exe, AppPaths.Default));
    }

    [Fact]
    public void Another_data_folder_has_its_own_entry_and_passes_the_folder()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "e2e data"));

        Assert.Equal($"RepoBoard ({paths.Id})", Autostart.ValueName(paths));
        Assert.Equal($"\"{Exe}\" --minimized \"--data-dir={paths.Root}\"", Autostart.Command(Exe, paths));
    }

    [Fact]
    public void The_uninstaller_finds_the_entries_of_its_own_program_only()
    {
        var other = new AppPaths(Path.Combine(Path.GetTempPath(), "e2e data"));

        Assert.True(Autostart.Starts(Autostart.Command(Exe, AppPaths.Default), Exe));
        Assert.True(Autostart.Starts(Autostart.Command(Exe.ToUpperInvariant(), other), Exe)); // Windows paths ignore case
        Assert.False(Autostart.Starts(Autostart.Command(Exe + ".old", AppPaths.Default), Exe));
        Assert.False(Autostart.Starts(null, Exe));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData("with space", "\"with space\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"C:\dir with space\", @"""C:\dir with space\\""")] // the last backslash must not escape the quote
    [InlineData(@"a\b c", @"""a\b c""")] // backslashes not before a quote stay as they are
    public void Arguments_are_quoted_as_Windows_splits_them(string argument, string expected) =>
        Assert.Equal(expected, Autostart.Quote(argument));

    [Theory]
    [InlineData(null, true)]
    [InlineData(new byte[0], true)]
    [InlineData(new byte[] { 2, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 3, 0, 0, 0 }, false)] // switched off in the Task Manager
    [InlineData(new byte[] { 7, 0, 0, 0 }, false)]
    public void The_task_manager_can_switch_the_entry_off(byte[]? approved, bool expected) =>
        Assert.Equal(expected, Autostart.IsApproved(approved));

    [Fact]
    public void A_data_folder_has_one_id_whatever_its_spelling()
    {
        var root = Path.Combine(Path.GetTempPath(), "RepoBoard-Id");

        Assert.Equal(new AppPaths(root).Id, new AppPaths(root + Path.DirectorySeparatorChar).Id);
        Assert.NotEqual(new AppPaths(root).Id, new AppPaths(root + "2").Id);
        Assert.False(new AppPaths(root).IsDefault);
    }
}
