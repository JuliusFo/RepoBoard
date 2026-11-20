using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RepoBoard.App.Services;
using RepoBoard.Core;
using RepoBoard.Core.Board;
using RepoBoard.Core.Git;
using RepoBoard.Core.Repos;
using RepoBoard.Core.Resources;
using RepoBoard.Core.Sync;
using RepoBoard.UI.Resources;
using RepoBoard.UI.State;
using Serilog;
using Serilog.Events;

namespace RepoBoard.App;

public partial class App : Application
{
    private readonly AppPaths _paths;
    private readonly string[] _args;
    private IHost? _host;
    private MainWindow? _window;
    private TrayIcon? _tray;
    private InstanceSignal? _signal;
    private SyncNotifier? _notifier;
    private UpdateState? _updates;
    private AppExit? _appExit;
    private bool _exiting;
    private bool _shutdownRequested;

    /// <summary>Started by <see cref="Program.Main"/>, which holds the data folder's instance lock.</summary>
    internal App(AppPaths paths, string[] args)
    {
        _paths = paths;
        _args = args;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .WriteTo.File(Path.Combine(_paths.LogsDirectory, "repoboard-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
            .CreateLogger();
        RegisterGlobalExceptionHandlers(); // before anything that can fail
        Start(); // the UI culture is set in Program.Main
    }

    /// <summary>A start that fails says so and ends the process, instead of a process without a window.</summary>
    private async void Start()
    {
        try
        {
            await StartAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "RepoBoard could not start");
            MessageBox.Show(TextFormat.Format(BoardText.App_StartFailed, ex.Message, _paths.LogsDirectory), "RepoBoard", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async Task StartAsync()
    {
        var theme = WindowTheme.Create(_args);

        // Without the defaults: they read appsettings*.json from the working folder, take DOTNET_ENVIRONMENT and log to the
        // console and the event log beside Serilog (FerretSharp review 3.40.0, A33).
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Services.AddSerilog();
        builder.Services.Configure<ConsoleLifetimeOptions>(o => o.SuppressStatusMessages = true); // no "Press Ctrl+C" in the log
        builder.Services.AddWpfBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif
        builder.Services.AddSingleton(_paths);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(theme);
        builder.Services.AddSingleton(new GitRunner());
        builder.Services.AddSingleton<GitClient>();
        builder.Services.AddSingleton(services => new ConfigStore(_paths.ConfigFile, services.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<BoardService>();
        builder.Services.AddSingleton<IDesktop, WpfDesktop>();
        builder.Services.AddSingleton<IAutostart, RegistryAutostart>();
        builder.Services.AddSingleton<BoardNavigation>();
        builder.Services.AddSingleton<IAppUpdater>(new VelopackUpdater(_args));
        builder.Services.AddSingleton<AppExit>();
        builder.Services.AddSingleton<UpdateState>();
        builder.Services.AddHostedService<AutoSyncService>();
        builder.Services.AddSingleton<MainWindow>();
        _host = builder.Build();

        var board = _host.Services.GetRequiredService<BoardService>();
        await board.LoadAsync(CancellationToken.None);
        var window = _window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Closing += OnWindowClosing;
        SessionEnding += (_, _) => _exiting = true;
        _tray = new TrayIcon(board, ShowWindow, ExitApp, _host.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TrayIcon>>());
        _notifier = new SyncNotifier(board, _host.Services.GetRequiredService<TimeProvider>());
        _notifier.Show += n => Dispatcher.BeginInvoke(() => Notify(n));
        _signal = new InstanceSignal(_paths, request => Dispatcher.BeginInvoke(request == InstanceRequest.Exit ? ExitApp : ShowWindow));
        _appExit = _host.Services.GetRequiredService<AppExit>();
        _appExit.Requested += () => Dispatcher.BeginInvoke(ExitApp); // "Restart now" into an update (WP-10)
        var updates = _updates = _host.Services.GetRequiredService<UpdateState>();
        updates.Announced += update => Dispatcher.BeginInvoke(() => NotifyUpdate(update));

        // Started with Windows: in the notification area only (decision of the user); the page loads with the first show.
        if (!CommandLine.Has(_args, CommandLine.Minimized))
        {
            window.Show();
        }

        await _host.StartAsync();
        Log.Information("RepoBoard {Version} started on {DataFolder}", AppVersion.Current, _paths.Root);

        // Never throws: a failed check is a note in the settings.
        await updates.StartAsync();
    }

    /// <summary>From the tray, a notification or a second start: the window comes back as it was, in front.</summary>
    private void ShowWindow()
    {
        if (_window is not { } window)
        {
            return;
        }

        window.Show();
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    /// <summary>
    /// Closing keeps RepoBoard in the notification area (default, decision of the user) – the first time with a hint – or
    /// ends it, as set. The tray's Exit and the end of the Windows session always end it.
    /// </summary>
    private async void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var board = _host!.Services.GetRequiredService<BoardService>();
        if (_exiting || !board.Settings.CloseToTray)
        {
            ExitApp();
            return;
        }

        e.Cancel = true;
        _window!.Hide();
        if (board.Settings.TrayHintShown)
        {
            return;
        }

        _tray?.Notify(BoardText.Tray_HintTitle, BoardText.Tray_HintText, ActivityKind.Info, null);
        try
        {
            await board.UpdateSettingsAsync(s => s with { TrayHintShown = true }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Saving that the tray hint was shown failed");
        }
    }

    /// <summary>Not over the window the user is looking at: there the activity log shows it.</summary>
    private void Notify(SyncNotification notification)
    {
        if (_window is { IsVisible: true, IsActive: true, WindowState: not WindowState.Minimized })
        {
            return;
        }

        var navigation = _host!.Services.GetRequiredService<BoardNavigation>();
        _tray?.Notify(notification.Title, notification.Text, notification.Worst, notification.RepoPath is { } repo ? () => navigation.Open(repo) : null);
    }

    /// <summary>
    /// A newer RepoBoard, once per version (WP-10): RepoBoard often runs in the notification area only, where the top bar is
    /// not seen. Not over the window the user is looking at, and not with notifications off. A click opens the dialog.
    /// </summary>
    private void NotifyUpdate(AppUpdate update)
    {
        var board = _host!.Services.GetRequiredService<BoardService>();
        if (board.Settings.Notifications == NotificationLevel.Off
            || _window is { IsVisible: true, IsActive: true, WindowState: not WindowState.Minimized })
        {
            return;
        }

        _tray?.Notify(TextFormat.Format(UpdateText.Notify_Title, update.Version), UpdateText.Notify_Text, ActivityKind.Info, () => _updates?.ShowDetails());
    }

    private void ExitApp()
    {
        _exiting = true;
        if (!_shutdownRequested)
        {
            _shutdownRequested = true;
            Shutdown();
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled exception on the UI thread");
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Fatal(args.ExceptionObject as Exception, "Unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
    }

    /// <summary>The host is disposed on a pool thread: the UI thread waits for it and must not be needed meanwhile.</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        // On the UI thread: the icon belongs to it (and would stay in the notification area until the mouse passes).
        _tray?.Dispose();
        _signal?.Dispose();
        _notifier?.Dispose();
        if (_host is { } host)
        {
            Task.Run(async () =>
            {
                await host.StopAsync(TimeSpan.FromSeconds(3));
                host.Dispose();
            }).GetAwaiter().GetResult();
        }

        // After the background work stopped: the updater waits for this process to end, installs and starts the new version.
        _appExit?.Exiting();

        Log.Information("RepoBoard ended");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
