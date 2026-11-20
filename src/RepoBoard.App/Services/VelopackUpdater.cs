using System.Collections.Concurrent;
using System.IO;
using RepoBoard.Core;
using RepoBoard.UI.State;
using Velopack;
using Velopack.Sources;

namespace RepoBoard.App.Services;

/// <summary>
/// Updates with Velopack (WP-10, ADR 0007; as FerretSharp's) from the releases of the public GitHub repository, without a
/// token (60 requests per hour and address). <c>--update-source=&lt;folder&gt;</c> reads a local folder instead, for testing
/// an update without GitHub.
/// </summary>
internal sealed class VelopackUpdater : IAppUpdater
{
    private readonly UpdateManager _manager;
    private readonly string[] _restartArguments;

    /// <summary>
    /// What the checks found, by version: the download asks for the version the UI shows, and a check given up after its
    /// time limit may still end later and must not swap it.
    /// </summary>
    private readonly ConcurrentDictionary<string, UpdateInfo> _found = new();

    public VelopackUpdater(string[] args)
    {
        var folder = CommandLine.ValueOf(args, CommandLine.UpdateSource);
        IUpdateSource source = string.IsNullOrWhiteSpace(folder)
            ? new GithubSource(ProjectSite.Url, accessToken: null, prerelease: false)
            : new SimpleFileSource(new DirectoryInfo(Path.GetFullPath(folder)));
        _manager = new UpdateManager(source);
        _restartArguments = [.. CommandLine.Restart(args)];
    }

    public bool IsInstalled => _manager.IsInstalled;

    /// <summary>Only the target's notes: what lay between is not known any more once the download waits for the restart.</summary>
    public AppUpdate? Downloaded => IsInstalled && _manager.UpdatePendingRestart is { } pending ? AppUpdate.From(Notes(pending)) : null;

    public async Task<AppUpdate?> CheckAsync(CancellationToken cancellationToken)
    {
        if (await _manager.CheckForUpdatesAsync() is not { } found)
        {
            return null;
        }

        var target = Notes(found.TargetFullRelease);
        _found[target.Version] = found;
        // The delta packages after the installed version up to the target each carry their version's notes. None without a
        // full package of the installed version (portable) or without a delta for the target: then only the target's notes.
        return AppUpdate.From(target, found.DeltasToTarget.Select(Notes));
    }

    public Task DownloadAsync(AppUpdate update, Action<int> progress, CancellationToken cancellationToken)
    {
        if (!_found.TryGetValue(update.Version, out var found))
        {
            throw new InvalidOperationException($"Update {update.Version} was not found by a check.");
        }

        return _manager.DownloadUpdatesAsync(found, progress, cancellationToken);
    }

    /// <summary>The updater waits up to 60 s for this process to end, installs and starts the new version with the window.</summary>
    public void ApplyAfterExit(AppUpdate update)
    {
        var asset = _manager.UpdatePendingRestart ?? throw new InvalidOperationException($"Update {update.Version} is not downloaded.");
        _manager.WaitExitThenApplyUpdates(asset, silent: false, restart: true, _restartArguments);
    }

    private static VersionNotes Notes(VelopackAsset asset) => new(asset.Version.ToString(), asset.NotesMarkdown);
}
