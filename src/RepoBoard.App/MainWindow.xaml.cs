using System.Globalization;
using System.Windows;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Extensions.Logging;
using RepoBoard.App.Services;
using RepoBoard.Core.Board;

namespace RepoBoard.App;

public partial class MainWindow : Window
{
    private static readonly TimeSpan RefreshOnActivation = TimeSpan.FromSeconds(5);
    private readonly ILogger<MainWindow> _logger;
    private DateTime _lastActivated;

    public MainWindow(IServiceProvider services, WindowTheme theme, WebViewCapture capture, BoardService board, ILogger<MainWindow> logger)
    {
        _logger = logger;
        theme.PrepareWebViewBackground();
        InitializeComponent();
        // The window fits the work area: 1480 x 860 is taller than it on a 1080p screen at 125 % (review 0.4.0, A37).
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        WebView.Services = services;
        theme.Attach(this);
        WebView.BlazorWebViewInitialized += (_, e) =>
        {
            theme.AttachWebView(e.WebView.CoreWebView2);
            capture.Attach(e.WebView); // each frame shown once it is really there (ADR 0009)
            // The page's language is the UI's, so a screen reader reads German as German (review 0.4.0, A30). From the host:
            // the page's CSP allows no inline script.
            var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            if (language.All(char.IsAsciiLetterLower))
            {
                e.WebView.CoreWebView2.NavigationCompleted += async (_, _) =>
                {
                    try
                    {
                        await e.WebView.CoreWebView2.ExecuteScriptAsync($"document.documentElement.lang = '{language}';");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Setting the page's language failed");
                    }
                };
            }
        };

        // Only web links leave the app: BlazorWebView opens every other URL with the system's default handler – a file
        // dropped on the window would be opened or run (FerretSharp review 3.31.0, A39).
        WebView.UrlLoading += (_, e) =>
        {
            if (e.UrlLoadingStrategy == UrlLoadingStrategy.OpenExternally && e.Url.Scheme != Uri.UriSchemeHttps)
            {
                e.UrlLoadingStrategy = UrlLoadingStrategy.CancelLoad;
            }
        };

        // Back in the window after working in the IDE: the status at once, not with the next tick.
        Activated += async (_, _) =>
        {
            if (DateTime.UtcNow - _lastActivated < RefreshOnActivation)
            {
                return;
            }

            _lastActivated = DateTime.UtcNow;
            try
            {
                await board.RefreshAllAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Refresh on activation failed");
            }
        };
    }
}
