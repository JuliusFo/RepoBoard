namespace RepoBoard.UI.State;

/// <summary>What only the host can do (CLAUDE.md §4): native dialogs and starting other programs. Implemented in the App.</summary>
public interface IDesktop
{
    /// <summary>A folder the user picks in the native dialog; null if cancelled.</summary>
    Task<string?> PickFolderAsync(string title);

    void OpenFolder(string path);

    void OpenTerminal(string path);

    /// <summary>VS Code with the folder <paramref name="path"/>; with <paramref name="file"/> (relative, from the status) that file opened in it.</summary>
    void OpenInEditor(string path, string? file = null);
}
