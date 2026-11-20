namespace RepoBoard.UI.State;

/// <summary>
/// The host asks the page to open a repository (a click on a notification). The request waits until the page takes it:
/// started in the notification area, the page does not exist yet when the first notification is clicked.
/// </summary>
public sealed class BoardNavigation
{
    private string? _pending;

    /// <summary>Raised on the caller's thread after each <see cref="Open"/>.</summary>
    public event Action? Requested;

    public void Open(string repoPath)
    {
        Volatile.Write(ref _pending, repoPath);
        Requested?.Invoke();
    }

    /// <summary>The repository to open, once; null if nothing is waiting.</summary>
    public string? Take() => Interlocked.Exchange(ref _pending, null);
}
