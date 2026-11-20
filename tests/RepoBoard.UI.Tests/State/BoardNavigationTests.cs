using RepoBoard.UI.State;

namespace RepoBoard.UI.Tests.State;

/// <summary>How a click on a notification reaches the page (ADR 0006).</summary>
public sealed class BoardNavigationTests
{
    [Fact]
    public void A_request_to_open_a_repository_waits_until_the_page_takes_it_once()
    {
        var navigation = new BoardNavigation();
        var raised = 0;
        navigation.Requested += () => raised++;

        navigation.Open("a");
        navigation.Open("b"); // the newer click wins

        Assert.Equal(2, raised);
        Assert.Equal("b", navigation.Take());
        Assert.Null(navigation.Take());
    }
}
