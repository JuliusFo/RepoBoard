using Microsoft.Extensions.Time.Testing;
using RepoBoard.Core.Repos;

namespace RepoBoard.Core.Tests.Repos;

/// <summary>The repository list on disk: never lost, never overwritten when this version cannot read it.</summary>
public sealed class ConfigStoreTests : IDisposable
{
    private readonly TestFolder _folder = new();
    private readonly FakeTimeProvider _time = new();
    private readonly ConfigStore _store;

    public ConfigStoreTests() => _store = new ConfigStore(_folder.Combine("config.json"), _time);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_missing_file_gives_the_decided_defaults()
    {
        var load = await _store.LoadAsync(Ct);

        Assert.Empty(load.Config.Repos);
        Assert.Equal(5, load.Config.AutoSyncMinutes);
        Assert.True(load.Config.Settings.CloseToTray);
        Assert.Equal(NotificationLevel.PullsAndProblems, load.Config.Settings.Notifications);
        Assert.False(load.Config.Settings.TrayHintShown);
        Assert.True(load.CanSave);
        Assert.Null(load.Problem);
    }

    [Fact]
    public async Task The_list_round_trips()
    {
        var config = new BoardConfig
        {
            Repos = [new RepoEntry(_folder.Combine("a"), AutoFetch: true), new RepoEntry(_folder.Combine("b"), true, true)],
            AutoSyncMinutes = 15,
            Settings = new AppSettings { CloseToTray = false, Notifications = NotificationLevel.Problems, TrayHintShown = true },
        };

        await _store.SaveAsync(config, Ct);
        var load = await _store.LoadAsync(Ct);

        Assert.Equal(config.Repos, load.Config.Repos);
        Assert.Equal(15, load.Config.AutoSyncMinutes);
        Assert.Equal(config.Settings, load.Config.Settings);
    }

    [Fact]
    public async Task A_file_of_a_newer_version_is_not_overwritten()
    {
        File.WriteAllText(_store.File, """{ "version": 99, "repos": [] }""");

        var load = await _store.LoadAsync(Ct);

        Assert.False(load.CanSave);
        Assert.NotNull(load.Problem);
    }

    [Fact]
    public async Task A_broken_file_is_kept_as_a_copy_before_the_list_starts_empty()
    {
        File.WriteAllText(_store.File, "{ not json");

        var load = await _store.LoadAsync(Ct);

        Assert.True(load.CanSave);
        Assert.Empty(load.Config.Repos);
        var copy = Assert.Single(Directory.GetFiles(_folder.Path, "config.json.bad-*"));
        Assert.Equal("{ not json", File.ReadAllText(copy));
    }

    /// <summary>
    /// A file edited by hand: null entries, empty and relative paths, a repository twice, an interval not offered, a
    /// notification level that does not exist.
    /// </summary>
    [Fact]
    public async Task A_hand_edited_file_is_cleaned_up_on_reading()
    {
        var path = _folder.Combine("a").Replace("\\", "\\\\", StringComparison.Ordinal);
        File.WriteAllText(_store.File, $$"""
            { "version": 1, "autoSyncMinutes": 7, "settings": { "notifications": 9, "closeToTray": false }, "repos": [
              null, { "path": "" }, { "path": "relative" }, { "path": "{{path}}", "autoFetch": true }, { "path": "{{path}}" } ] }
            """);

        var load = await _store.LoadAsync(Ct);

        var entry = Assert.Single(load.Config.Repos);
        Assert.True(entry.AutoFetch);
        Assert.Equal(5, load.Config.AutoSyncMinutes);
        Assert.Equal(new AppSettings { CloseToTray = false }, load.Config.Settings);
    }

    [Fact]
    public async Task A_file_from_before_the_settings_gets_the_defaults()
    {
        File.WriteAllText(_store.File, """{ "version": 1, "repos": [], "settings": null }""");

        var load = await _store.LoadAsync(Ct);

        Assert.Equal(new AppSettings(), load.Config.Settings);
    }

    public void Dispose() => _folder.Dispose();
}
