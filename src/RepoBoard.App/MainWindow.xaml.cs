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
        WebView.Services = services;
        theme.Attach(this);
        WebView.BlazorWebViewInitialized += (_, e) =>
        {
            theme.AttachWebView(e.WebView.CoreWebView2);
            capture.Attach(e.WebView); // each frame shown once it is really there (ADR 0008)
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
