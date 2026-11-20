using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using RepoBoard.Core;
using RepoBoard.UI.State;

namespace RepoBoard.App.Services;

/// <summary>
/// Start with Windows as a value under <c>HKCU\…\Run</c> (ADR 0006): no admin rights, no shortcut file, and the Task
/// Manager lists it under "Startup apps", where the user can switch it off – that counts as off here.
/// </summary>
internal sealed class RegistryAutostart(AppPaths paths, ILogger<RegistryAutostart> logger) : IAutostart
{
    private string Name => Autostart.ValueName(paths);

    public bool IsOn
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(Autostart.RunKey);
            if (run?.GetValue(Name) is not string)
            {
                return false;
            }

            using var approved = Registry.CurrentUser.OpenSubKey(Autostart.ApprovedKey);
            return Autostart.IsApproved(approved?.GetValue(Name) as byte[]);
        }
    }

    /// <summary>
    /// On writes the current program (a moved RepoBoard is found again) and clears a switch-off from the Task Manager, so
    /// the setting means what it says; off removes only RepoBoard's own value.
    /// </summary>
    public void Set(bool on)
    {
        using var run = Registry.CurrentUser.CreateSubKey(Autostart.RunKey);
        if (on)
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("The program path is unknown.");
            run.SetValue(Name, Autostart.Command(exe, paths));
            using var approved = Registry.CurrentUser.OpenSubKey(Autostart.ApprovedKey, writable: true);
            approved?.DeleteValue(Name, throwOnMissingValue: false);
        }
        else
        {
            run.DeleteValue(Name, throwOnMissingValue: false);
        }

        logger.LogInformation("Start with Windows {State} ({Name})", on ? "on" : "off", Name);
    }

    /// <summary>
    /// The uninstaller (WP-10): removes every start entry of <paramref name="exe"/>, whatever its data folder – Windows would
    /// otherwise try to start a program that is gone at every sign-in. Never throws: the uninstall goes on.
    /// </summary>
    public static void RemoveAllOf(string? exe)
    {
        if (string.IsNullOrEmpty(exe))
        {
            return;
        }

        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(Autostart.RunKey, writable: true);
            if (run is null)
            {
                return;
            }

            using var approved = Registry.CurrentUser.OpenSubKey(Autostart.ApprovedKey, writable: true);
            foreach (var name in run.GetValueNames().Where(n => Autostart.Starts(run.GetValue(n) as string, exe)))
            {
                run.DeleteValue(name, throwOnMissingValue: false);
                approved?.DeleteValue(name, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            // Nothing to log to: the uninstaller runs before the log exists.
        }
    }
}
