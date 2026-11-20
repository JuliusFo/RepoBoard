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
        builder.Services.AddHostedService<AutoSyncService>();
        builder.Services.AddSingleton<MainWindow>();
        _host = builder.Build();

        await _host.Services.GetRequiredService<BoardService>().LoadAsync(CancellationToken.None);
        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
        await _host.StartAsync();
        Log.Information("RepoBoard {Version} started on {DataFolder}", typeof(App).Assembly.GetName().Version, _paths.Root);
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
        if (_host is { } host)
        {
            Task.Run(async () =>
            {
                await host.StopAsync(TimeSpan.FromSeconds(3));
                host.Dispose();
            }).GetAwaiter().GetResult();
        }

        Log.Information("RepoBoard ended");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
