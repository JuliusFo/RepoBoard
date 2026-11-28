using System.IO;
using System.Runtime.InteropServices;
using RepoBoard.Core;

namespace RepoBoard.App.Services;

internal enum InstanceRequest
{
    /// <summary>A second start: show the window, which may be hidden in the notification area.</summary>
    Show,

    /// <summary><c>--exit</c>: end as from the tray's Exit (E2E, later an installer that replaces the program).</summary>
    Exit,
}

/// <summary>
/// Another start on the same data folder asks the running instance to show its window or to end (ADR 0006). Needed because
/// the window may be hidden in the notification area: then the process has no main window another process
/// could bring forward or close. A named event per data folder and request in the session's namespace; whoever sets it
/// can do no more than the tray's menu.
/// </summary>
internal sealed class InstanceSignal : IDisposable
{
    private readonly List<(EventWaitHandle Event, RegisteredWaitHandle Wait)> _listeners = [];

    /// <summary>Listens for the requests; <paramref name="onRequest"/> runs on a pool thread.</summary>
    public InstanceSignal(AppPaths paths, Action<InstanceRequest> onRequest)
    {
        foreach (var request in Enum.GetValues<InstanceRequest>())
        {
            var signal = new EventWaitHandle(false, EventResetMode.AutoReset, Name(paths, request));
            var wait = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => onRequest(request), null, Timeout.Infinite, executeOnlyOnce: false);
            _listeners.Add((signal, wait));
        }
    }

    /// <summary>
    /// Signals the running instance; false if it does not listen (yet). This process was just started by the user, so it
    /// may hand the right to take the foreground to <paramref name="processId"/> first.
    /// </summary>
    public static bool Send(AppPaths paths, InstanceRequest request, int? processId)
    {
        EventWaitHandle? signal;
        try
        {
            if (!EventWaitHandle.TryOpenExisting(Name(paths, request), out signal))
            {
                return false;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
        {
            // The running instance is elevated (an admin terminal): its event is not ours to open. Unhandled, this ended the
            // start without a word or a log (review 0.4.0, A33); the caller says "already running".
            return false;
        }

        using (signal)
        {
            if (processId is { } id && request == InstanceRequest.Show)
            {
                AllowSetForegroundWindow(id);
            }

            return signal.Set();
        }
    }

    private static string Name(AppPaths paths, InstanceRequest request) =>
        $@"Local\{AppPaths.AppFolderName}-{paths.Id}-{request.ToString().ToLowerInvariant()}";

    public void Dispose()
    {
        foreach (var (signal, wait) in _listeners)
        {
            wait.Unregister(null);
            signal.Dispose();
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
