using System.Globalization;
using System.IO;
using System.Windows;
using RepoBoard.App.Services;
using RepoBoard.Core;
using RepoBoard.Core.IO;
using RepoBoard.Core.Resources;
using RepoBoard.UI.Resources;

namespace RepoBoard.App;

/// <summary>Own entry point (as in FerretSharp): one instance per data folder, then the WPF app.</summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // The UI culture only synchronously, before any await: it belongs to the execution context, and a change inside an
        // async method is undone when it returns – Blazor would render in the Windows language again (FerretSharp ui.md).
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture = CommandLine.UiCulture(args, CultureInfo.CurrentUICulture);
        var paths = DataFolder(args);
        InstanceLock? instance = null;
        try
        {
            if (!InstanceLock.TryAcquire(paths, Environment.ProcessId, out instance, out var running))
            {
                // Two instances would fetch and pull the same repositories at once: the running one comes forward instead –
                // through its signal, as its window may be hidden in the notification area (ADR 0006).
                if (!ShowSignal.Send(paths, running) && !RunningInstance.BringForward(running))
                {
                    MessageBox.Show(TextFormat.Format(BoardText.App_AlreadyRunning, paths.Root), "RepoBoard", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                return 0;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The data folder cannot be written: the start reports that itself (log, config), with or without the lock.
        }

        using (instance)
        {
            var app = new App(paths, args);
            app.InitializeComponent();
            return app.Run();
        }
    }

    /// <summary><c>--data-dir=&lt;path&gt;</c> redirects the repository list and the logs (tests, E2E, separate profiles).</summary>
    private static AppPaths DataFolder(string[] args)
    {
        var dataDir = CommandLine.ValueOf(args, CommandLine.DataDir);
        return string.IsNullOrWhiteSpace(dataDir) ? AppPaths.Default : new AppPaths(Path.GetFullPath(dataDir));
    }
}
