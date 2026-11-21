using RepoBoard.App.Services;

namespace RepoBoard.App.Tests;

public sealed class WebViewCaptureTests
{
    /// <summary>
    /// The pinned WebView2 (Directory.Packages.props) has every private member the capture reaches by reflection. Fails when
    /// a package update renames one – in the app it would only be a warning in the log, and the delay would be back (ADR 0009).
    /// Not covered: a version that adds its frame handler as a lambda, which the takeover cannot remove (check per E2E).
    /// </summary>
    [Fact]
    public void The_pinned_WebView2_has_every_member_used()
    {
        Assert.Empty(WebViewCapture.MissingMembers());
    }
}
