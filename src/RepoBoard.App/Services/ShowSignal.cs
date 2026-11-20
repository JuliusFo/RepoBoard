using System.Runtime.InteropServices;
using RepoBoard.Core;

namespace RepoBoard.App.Services;

/// <summary>
/// A second start on the same data folder asks the running instance to show its window (ADR 0006). Needed because the
/// window may be hidden in the notification area: then the process has no main window <see cref="RunningInstance"/> could
/// bring forward. A named event per data folder in the session's namespace; whoever sets it can only make the window
/// appear.
/// </summary>
internal sealed class ShowSignal : IDisposable
{
    private readonly EventWaitHandle _event;
    private readonly RegisteredWaitHandle _wait;

    /// <summary>Listens for the signal; <paramref name="show"/> runs on a pool thread.</summary>
    public ShowSignal(AppPaths paths, Action show)
    {
        _event = new EventWaitHandle(false, EventResetMode.AutoReset, Name(paths));
        _wait = ThreadPool.RegisterWaitForSingleObject(_event, (_, _) => show(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>
    /// Signals the running instance; false if it does not listen (yet). This process was just started by the user, so it
    /// may hand the right to take the foreground to <paramref name="processId"/> first.
    /// </summary>
    public static bool Send(AppPaths paths, int? processId)
    {
        if (!EventWaitHandle.TryOpenExisting(Name(paths), out var signal))
        {
            return false;
        }

        using (signal)
        {
            if (processId is { } id)
            {
                AllowSetForegroundWindow(id);
            }

            return signal.Set();
        }
    }

    private static string Name(AppPaths paths) => $@"Local\{AppPaths.AppFolderName}-{paths.Id}-show";

    public void Dispose()
    {
        _wait.Unregister(null);
        _event.Dispose();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
