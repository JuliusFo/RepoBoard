namespace RepoBoard.UI.State;

/// <summary>What only the host can do (CLAUDE.md §4): native dialogs and starting other programs. Implemented in the App.</summary>
public interface IDesktop
{
    /// <summary>A folder the user picks in the native dialog, starting in <paramref name="start"/> (or the profile); null if cancelled.</summary>
    Task<string?> PickFolderAsync(string title, string? start);

    void OpenFolder(string path);

    void OpenTerminal(string path);

    void OpenInEditor(string path);
}
