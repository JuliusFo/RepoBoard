using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using RepoBoard.UI.State;

namespace RepoBoard.App.Services;

/// <summary>
/// The host's part of <see cref="IDesktop"/>. Programs start only with <see cref="ProcessStartInfo.ArgumentList"/> and a
/// repository path from the board – never a shell line built from text.
/// </summary>
internal sealed class WpfDesktop : IDesktop
{
    public Task<string?> PickFolderAsync(string title) =>
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dialog = new OpenFolderDialog { Title = title, InitialDirectory = DefaultFolder() };
            return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FolderName : null;
        }).Task;

    public void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false })?.Dispose();

    /// <summary>Windows Terminal if installed, PowerShell otherwise.</summary>
    public void OpenTerminal(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("wt.exe") { ArgumentList = { "-d", path }, UseShellExecute = false })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Process.Start(new ProcessStartInfo("powershell.exe") { WorkingDirectory = path, UseShellExecute = true })?.Dispose();
        }
    }

    /// <summary>
    /// VS Code through its command (<c>code.cmd</c>), without a console window. Through <c>cmd /c</c>, which parses the line
    /// itself: a path with <c>&amp;</c> or <c>"</c> is refused rather than passed on (a folder name could carry a command).
    /// </summary>
    public void OpenInEditor(string path)
    {
        if (path.IndexOfAny(['"', '&', '|', '<', '>', '^', '%']) >= 0)
        {
            throw new InvalidOperationException($"The path cannot be passed to VS Code safely: {path}");
        }

        Process.Start(new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", "code", path }, UseShellExecute = false, CreateNoWindow = true })?.Dispose();
    }

    private static string DefaultFolder() =>
        Directory.Exists(@"C:\Dev-Repos") ? @"C:\Dev-Repos" : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
