using Microsoft.Extensions.Logging;
using RepoBoard.Core.Board;
using RepoBoard.Core.Repos;
using RepoBoard.Core.Resources;
using RepoBoard.UI.Resources;

namespace RepoBoard.UI.State;

public enum UpdatePhase
{
    /// <summary>Not an installed RepoBoard (a build, a published folder): no checks, the settings say why.</summary>
    NotInstalled,

    /// <summary>Not checked in this session (the check at start is off).</summary>
    Idle,

    Checking,

    UpToDate,

    /// <summary>A newer version is out; the top bar says so.</summary>
    Available,

    Downloading,

    /// <summary>Downloaded; a restart installs it.</summary>
    Ready,

    /// <summary>The check failed (offline, VPN, rate limit): a harmless note in the settings, nothing in the top bar.</summary>
    CheckFailed,

    /// <summary>The download failed; the top bar offers to try again.</summary>
    DownloadFailed,
}

/// <summary>
/// Update of the app (WP-10, ADR 0007; from FerretSharp WP-32 with its addenda): only reports. The check at start and while
/// the app runs (if on, every <see cref="AppSettings.UpdateCheckHours"/>) and "Check now" look for a newer version;
/// downloading waits for the click on "Download", installing for a second click on "Restart now", which ends the app
/// (<see cref="AppExit"/>). A version released after the one reported replaces it: while the app runs (a silent check) and
/// right before the download, so the click on "Download" never installs one that is already outdated. Each newer version
/// is announced once (<see cref="Announced"/>, a Windows notification - RepoBoard often runs in the notification area only).
/// </summary>
public sealed class UpdateState(IAppUpdater updater, BoardService board, AppExit exit, ILogger<UpdateState> logger, TimeProvider? time = null) : IDisposable
{
    /// <summary>A check that gets no answer (a stalled VPN) ends after this; the next start or "Check now" tries again.</summary>
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How often the app looks whether the next check while it runs is due. The interval counts from the last check on the
    /// clock, not in timer ticks: a check that is due after a sleep of the PC comes within this time after waking up, and a
    /// changed setting needs no new timer.
    /// </summary>
    public static readonly TimeSpan BackgroundTick = TimeSpan.FromMinutes(5);

    /// <summary>
    /// A failed check without a known update is tried again after this, whatever the interval – also with "never", as it
    /// stands for the check at start: started with Windows, the network is often not there yet (Review WP-10). Not every
    /// tick: a rate limit of GitHub (60 requests per hour) would only get worse.
    /// </summary>
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(15);

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>One check at a time: "Check now" and the checks while running skip if one runs, the download waits for it.</summary>
    private readonly SemaphoreSlim _checks = new(1, 1);

    private CancellationTokenSource? _download;
    private ITimer? _background;
    private DateTimeOffset _started;

    public UpdatePhase Phase { get; private set; } = updater.IsInstalled ? UpdatePhase.Idle : UpdatePhase.NotInstalled;

    /// <summary>The newer version (available, downloading, ready or failed to download); null otherwise.</summary>
    public AppUpdate? Update { get; private set; }

    /// <summary>Download progress, 0–100.</summary>
    public int Progress { get; private set; }

    /// <summary>Why the last check or download failed.</summary>
    public string? Error { get; private set; }

    /// <summary>When the last check ended (also a failed one); null if none in this session.</summary>
    public DateTimeOffset? LastChecked { get; private set; }

    /// <summary>"Check now" is possible: installed, nothing running, nothing downloaded waiting for the restart.</summary>
    public bool CanCheck => Phase is UpdatePhase.Idle or UpdatePhase.UpToDate or UpdatePhase.Available or UpdatePhase.CheckFailed or UpdatePhase.DownloadFailed;

    /// <summary>The update dialog (release notes, "Download", "Restart now") is open - from the top bar, the settings or a notification.</summary>
    public bool DetailsOpen { get; private set; }

    /// <summary>Raised on any change, on the thread that made it (the download's progress comes from the thread pool).</summary>
    public event Action? Changed;

    /// <summary>
    /// A version newer than any announced before was found (<see cref="AppSettings.UpdateNotified"/>): the host shows a
    /// Windows notification. Raised on the thread of the check.
    /// </summary>
    public event Action<AppUpdate>? Announced;

    public void ShowDetails()
    {
        if (Update is not null)
        {
            DetailsOpen = true;
            Changed?.Invoke();
        }
    }

    /// <summary>Closes the dialog; a running download goes on (the top bar shows it).</summary>
    public void HideDetails()
    {
        DetailsOpen = false;
        Changed?.Invoke();
    }

    /// <summary>
    /// At start: a download left from the last session waits for the restart; otherwise check if the setting says so. Then
    /// the checks while the app runs begin (<see cref="BackgroundTick"/>).
    /// </summary>
    public async Task StartAsync()
    {
        if (Phase != UpdatePhase.Idle || _background is not null)
        {
            return;
        }

        _started = _time.GetLocalNow();
        _background = _time.CreateTimer(_ => CheckIfDue(), null, BackgroundTick, BackgroundTick);
        if (Downloaded() is { } downloaded)
        {
            Update = downloaded;
            Set(UpdatePhase.Ready);
            return;
        }

        if (board.Settings.CheckForUpdates)
        {
            await CheckAsync();
        }
    }

    /// <summary>A download that cannot be read is none: the start goes on and checks (Review WP-10).</summary>
    private AppUpdate? Downloaded()
    {
        try
        {
            return updater.Downloaded;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reading the update downloaded before failed");
            return null;
        }
    }

    /// <summary>
    /// Looks for a newer version. Never throws: a failure is <see cref="UpdatePhase.CheckFailed"/> with <see cref="Error"/> –
    /// unless an update is known already, which stays as it was.
    /// </summary>
    public async Task CheckAsync()
    {
        // "Check now" on the UI thread and a check while running on the timer's thread: one at a time.
        if (!CanCheck || !_checks.Wait(0))
        {
            return;
        }

        var (knownPhase, knownError) = (Phase, Error);
        Error = null;
        Set(UpdatePhase.Checking);
        try
        {
            await ReportAsync(await FindAsync(CancellationToken.None));
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "Update check failed");
            LastChecked = _time.GetLocalNow();
            if (Update is not null)
            {
                // A failed check takes nothing back: the update known before stays available (or failed to download).
                Error = knownError;
                Set(knownPhase);
            }
            else
            {
                Error = ex is TimeoutException ? TextFormat.Format(UpdateText.CheckTimedOut, CheckTimeout.TotalSeconds) : ex.Message;
                Set(UpdatePhase.CheckFailed);
            }
        }
        finally
        {
            _checks.Release();
        }
    }

    /// <summary>
    /// The check while the app runs, if it is on and due: the interval has passed since the last check (or the start), or
    /// <see cref="RetryAfterFailure"/> after a failed one. Not while the dialog is open, downloading or waiting for the
    /// restart; with an update available it checks silently (<see cref="CheckSilentlyAsync"/>).
    /// </summary>
    private void CheckIfDue()
    {
        var current = board.Settings;
        if (!current.CheckForUpdates || DetailsOpen
            || Phase is not (UpdatePhase.Idle or UpdatePhase.UpToDate or UpdatePhase.CheckFailed or UpdatePhase.Available))
        {
            return;
        }

        TimeSpan? interval = Phase == UpdatePhase.CheckFailed ? RetryAfterFailure
            : current.UpdateCheckHours > 0 ? TimeSpan.FromHours(current.UpdateCheckHours)
            : null;
        if (interval is { } due && _time.GetLocalNow() - (LastChecked ?? _started) >= due)
        {
            // Neither throws: a failed check is a note in the settings, or nothing while an update is available.
            _ = Phase == UpdatePhase.Available ? CheckSilentlyAsync() : CheckAsync();
        }
    }

    /// <summary>
    /// The check while an update is available: the top bar keeps showing it (no <see cref="UpdatePhase.Checking"/>), a
    /// newer version replaces it, a failed check changes nothing. A result that comes after the user acted (download,
    /// dialog) is dropped; the download checks again itself.
    /// </summary>
    private async Task CheckSilentlyAsync()
    {
        if (!_checks.Wait(0))
        {
            return;
        }

        try
        {
            var found = await FindAsync(CancellationToken.None);
            if (Phase == UpdatePhase.Available && !DetailsOpen)
            {
                await ReportAsync(found);
            }
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "Update check failed, update {Version} stays available", Update?.Version);
            LastChecked = _time.GetLocalNow();
        }
        finally
        {
            _checks.Release();
        }
    }

    private async Task<AppUpdate?> FindAsync(CancellationToken cancellationToken)
    {
        var found = await updater.CheckAsync(cancellationToken).WaitAsync(CheckTimeout, _time, cancellationToken);
        LastChecked = _time.GetLocalNow();
        return found;
    }

    /// <summary>A check that answered is the truth: a newer version, another one, or none (a release taken back).</summary>
    private async Task ReportAsync(AppUpdate? found)
    {
        if (found is not null && found.Version != Update?.Version)
        {
            logger.LogInformation("Update {Version} is available", found.Version);
        }

        Update = found;
        Set(found is null ? UpdatePhase.UpToDate : UpdatePhase.Available);
        if (found is not null)
        {
            await AnnounceAsync(found);
        }
    }

    /// <summary>
    /// Once per version, remembered across starts: started with Windows every morning, the same version would come up every
    /// day. A version taken back and released again under the same number is not announced again.
    /// </summary>
    private async Task AnnounceAsync(AppUpdate found)
    {
        if (board.Settings.UpdateNotified is { } notified && ReleaseVersion.Compare(found.Version, notified) <= 0)
        {
            return;
        }

        try
        {
            await board.UpdateSettingsAsync(s => s with { UpdateNotified = found.Version }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Announced anyway: at worst it comes again at the next start.
            logger.LogWarning(ex, "Saving that update {Version} was announced failed", found.Version);
        }

        Announced?.Invoke(found);
    }

    public void Dispose() => _background?.Dispose();

    /// <summary>
    /// The click on "Download": checks once more and downloads the newest version (cancellable, <see cref="CancelDownload"/>);
    /// then the update waits for "Restart now" (<see cref="Restart"/>, the user's decision in FerretSharp). Never throws: a failed
    /// download is <see cref="UpdatePhase.DownloadFailed"/>.
    /// </summary>
    public async Task DownloadAsync()
    {
        if (Update is not { } update || Phase is not (UpdatePhase.Available or UpdatePhase.DownloadFailed))
        {
            return;
        }

        var download = _download = new CancellationTokenSource();
        Progress = 0;
        Error = null;
        Set(UpdatePhase.Downloading);
        try
        {
            if (await NewestAsync(update, download.Token) is not { } newest)
            {
                // Taken back since the last check: nothing to download, and the dialog has nothing left to show.
                DetailsOpen = false;
                Set(UpdatePhase.UpToDate);
                return;
            }

            update = newest;
            await updater.DownloadAsync(update, ReportProgress, download.Token);
        }
        catch (OperationCanceledException) when (download.IsCancellationRequested)
        {
            logger.LogInformation("Download of update {Version} canceled", update.Version);
            Set(UpdatePhase.Available);
            return;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Download of update {Version} failed", update.Version);
            Error = ex.Message;
            Set(UpdatePhase.DownloadFailed);
            return;
        }
        finally
        {
            _download = null;
            download.Dispose();
        }

        logger.LogInformation("Update {Version} downloaded", update.Version);
        Progress = 100;
        Set(UpdatePhase.Ready);
    }

    /// <summary>
    /// The check right before the download: a version released since the last check is downloaded instead of the one shown
    /// (0.2 to 0.4 rather than 0.3 now and 0.4 after the restart), the dialog switches to its notes. If the check fails
    /// or gets no answer, the known version.
    /// </summary>
    private async Task<AppUpdate?> NewestAsync(AppUpdate known, CancellationToken cancellationToken)
    {
        await _checks.WaitAsync(cancellationToken); // a check while running ends within CheckTimeout
        try
        {
            var found = await FindAsync(cancellationToken);
            if (found?.Version != known.Version)
            {
                logger.LogInformation("Update {Version} replaces {Known} before the download", found?.Version ?? "(none)", known.Version);
                Update = found;
                Changed?.Invoke();
            }

            return found;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(ex, "Update check before the download failed, downloading the known {Version}", known.Version);
            LastChecked = _time.GetLocalNow();
            return known;
        }
        finally
        {
            _checks.Release();
        }
    }

    public void CancelDownload() => _download?.Cancel();

    /// <summary>
    /// Ends the app as the tray's Exit does; once it really ends, the updater installs the downloaded version and starts it
    /// with the window shown (<see cref="Core.CommandLine.Restart"/>).
    /// </summary>
    public void Restart()
    {
        if (Phase != UpdatePhase.Ready || Update is not { } update)
        {
            return;
        }

        DetailsOpen = false;
        Changed?.Invoke();
        exit.Request(() =>
        {
            try
            {
                logger.LogInformation("Restarting into update {Version}", update.Version);
                updater.ApplyAfterExit(update);
            }
            catch (Exception ex)
            {
                // The app ends anyway; the download stays and is offered again at the next start.
                logger.LogError(ex, "Starting the update to {Version} failed", update.Version);
            }
        });
    }

    private void ReportProgress(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        if (percent != Progress)
        {
            Progress = percent;
            Changed?.Invoke();
        }
    }

    private void Set(UpdatePhase phase)
    {
        Phase = phase;
        Changed?.Invoke();
    }
}
