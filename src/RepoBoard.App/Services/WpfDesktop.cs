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
    /// VS Code through its command (<c>code.cmd</c>), without a console window: the folder, and a file of it (a conflicted file,
    /// WP-07 – VS Code offers its merge editor there). Through <c>cmd /c</c>, which parses the line itself: a path cmd would
    /// read as an instruction is refused rather than passed on, and so is a file outside the folder; a missing command says
    /// so – cmd's own "not recognized" went nowhere (review 0.4.0, A34).
    /// </summary>
    public void OpenInEditor(string path, string? file = null)
    {
        var start = new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", "code", Checked(path) }, UseShellExecute = false, CreateNoWindow = true };
        if (file is not null)
        {
            var full = EditorPath.FileIn(path, file) ?? throw new InvalidOperationException($"The file is not inside the repository: {file}");
            start.ArgumentList.Add(Checked(full));
        }

        if (DesktopPaths.FindOnPath("code", Environment.GetEnvironmentVariable("PATH"), Environment.GetEnvironmentVariable("PATHEXT")) is null)
        {
            throw new InvalidOperationException(BoardText.Editor_NotFound);
        }

        Process.Start(start)?.Dispose();

        static string Checked(string value) => DesktopPaths.SafeForCmd(value)
            ? value
            : throw new InvalidOperationException(TextFormat.Format(BoardText.Editor_UnsafePath, value));
    }
}
