using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using RepoBoard.Core.Resources;
using RepoBoard.UI.Resources;
using RepoBoard.UI.State;

namespace RepoBoard.App.Services;

/// <summary>
/// The host's part of <see cref="IDesktop"/>. Programs start only with <see cref="ProcessStartInfo.ArgumentList"/> and a
/// repository path from the board – never a shell line built from text. The decisions about paths are in
/// <see cref="DesktopPaths"/>, where they are tested.
/// </summary>
internal sealed class WpfDesktop : IDesktop
{
    public Task<string?> PickFolderAsync(string title, string? start) =>
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dialog = new OpenFolderDialog
            {
                Title = title,
                InitialDirectory = start is not null && Directory.Exists(start) ? start : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };
            return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FolderName : null;
        }).Task;

    public void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false })?.Dispose();

    /// <summary>
    /// Windows Terminal if installed, PowerShell otherwise. The folder goes to Windows Terminal as its working folder
    /// (<c>-d .</c>), not as an argument: it reads <c>;</c> in its arguments as the start of another command, and a folder
    /// named <c>tools;calc</c> would open a second tab running <c>calc</c> (review 0.4.0, A8).
    /// </summary>
    public void OpenTerminal(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("wt.exe") { ArgumentList = { "-d", "." }, WorkingDirectory = path, UseShellExecute = false })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Process.Start(new ProcessStartInfo("powershell.exe") { WorkingDirectory = path, UseShellExecute = true })?.Dispose();
        }
    }

    /// <summary>
    /// VS Code through its command (<c>code.cmd</c>), without a console window. Through <c>cmd /c</c>, which parses the line
    /// itself: a path cmd would read as an instruction is refused rather than passed on, and a missing command says so –
    /// cmd's own "not recognized" went nowhere (review 0.4.0, A34).
    /// </summary>
    public void OpenInEditor(string path)
    {
        if (!DesktopPaths.SafeForCmd(path))
        {
            throw new InvalidOperationException(TextFormat.Format(BoardText.Editor_UnsafePath, path));
        }

        if (DesktopPaths.FindOnPath("code", Environment.GetEnvironmentVariable("PATH"), Environment.GetEnvironmentVariable("PATHEXT")) is null)
        {
            throw new InvalidOperationException(BoardText.Editor_NotFound);
        }

        Process.Start(new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", "code", path }, UseShellExecute = false, CreateNoWindow = true })?.Dispose();
    }
}
