namespace RepoBoard.UI.State;

/// <summary>
/// The page ends the app (WP-10: "Restart now" into an update). In FerretSharp the <c>ExitGuard</c> asks about uncommitted
/// changes first; RepoBoard has nothing to ask about, so it ends as the tray's Exit does. The host listens to
/// <see cref="Requested"/> and calls <see cref="Exiting"/> once the app really ends – after the background work stopped.
/// </summary>
public sealed class AppExit
{
    private Action? _onExit;

    /// <summary>Raised on the caller's thread: the host ends the app.</summary>
    public event Action? Requested;

    /// <summary>Ends the app; <paramref name="onExit"/> runs once it really ends (<see cref="Exiting"/>).</summary>
    public void Request(Action onExit)
    {
        ArgumentNullException.ThrowIfNull(onExit);
        Volatile.Write(ref _onExit, onExit);
        Requested?.Invoke();
    }

    /// <summary>Host: the app ends now (from any request or from the tray); runs what <see cref="Request"/> asked for, once.</summary>
    public void Exiting() => Interlocked.Exchange(ref _onExit, null)?.Invoke();
}
