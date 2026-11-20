using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using RepoBoard.Core;

namespace RepoBoard.App.Services;

/// <summary>
/// Light/dark for the whole window (from FerretSharp): native chrome (title bar, background behind the WebView) and the
/// WebView's <c>prefers-color-scheme</c>, which the CSS follows. Windows decides; <c>--theme=dark|light</c> overrides it
/// for the session (tests, screenshots). A setting of its own is in the backlog.
/// </summary>
public sealed class WindowTheme
{
    private const int DwmUseImmersiveDarkMode = 20;
    private static readonly Color DarkBackground = Color.FromRgb(0x14, 0x15, 0x18);
    private static readonly Color LightBackground = Color.FromRgb(0xf4, 0xf4, 0xf6);

    private WindowTheme(bool? sessionOverride) => SessionOverride = sessionOverride;

    /// <summary>true/false when forced via <c>--theme=dark|light</c>; null otherwise.</summary>
    public bool? SessionOverride { get; }

    public bool IsDark => SessionOverride ?? IsSystemDark();

    public static WindowTheme Create(IEnumerable<string> args) =>
        new(CommandLine.ValueOf(args, CommandLine.Theme)?.ToLowerInvariant() switch
        {
            "dark" => true,
            "light" => false,
            _ => null,
        });

    /// <summary>Must run before the WebView2 environment is created: no white flash on startup.</summary>
    public void PrepareWebViewBackground()
    {
        var c = IsDark ? DarkBackground : LightBackground;
        Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", $"FF{c.R:X2}{c.G:X2}{c.B:X2}");
    }

    public void Attach(Window window)
    {
        Apply(window);
        window.SourceInitialized += (_, _) => Apply(window);

        // The title bar follows Windows; the WebView does so by itself (PreferredColorScheme.Auto).
        UserPreferenceChangedEventHandler handler = (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General)
            {
                window.Dispatcher.Invoke(() => Apply(window));
            }
        };
        SystemEvents.UserPreferenceChanged += handler;
        window.Closed += (_, _) => SystemEvents.UserPreferenceChanged -= handler;
    }

    public void AttachWebView(CoreWebView2 webView) =>
        webView.Profile.PreferredColorScheme = SessionOverride switch
        {
            true => CoreWebView2PreferredColorScheme.Dark,
            false => CoreWebView2PreferredColorScheme.Light,
            null => CoreWebView2PreferredColorScheme.Auto,
        };

    private void Apply(Window window)
    {
        var dark = IsDark;
        window.Background = new SolidColorBrush(dark ? DarkBackground : LightBackground);
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            var value = dark ? 1 : 0;
            _ = DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref value, sizeof(int));
        }
    }

    private static bool IsSystemDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
