using RepoBoard.Core.IO;

namespace RepoBoard.Core.Tests.IO;

/// <summary>One answer to "the same path?" and "below that folder?" (review 0.4.0, B3).</summary>
public sealed class PathsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "repoboard-paths");

    [Fact]
    public void A_path_is_the_same_however_it_is_written()
    {
        var repo = Path.Combine(Root, "repo");

        Assert.True(Paths.Same(repo, repo + Path.DirectorySeparatorChar));
        Assert.True(Paths.Same(repo, Path.Combine(Root, "x", "..", "repo")));
        Assert.True(Paths.Same(repo, repo.Replace(Path.DirectorySeparatorChar, '/')));
        Assert.False(Paths.Same(repo, Path.Combine(Root, "repo2")));
        Assert.Equal(OperatingSystem.IsWindows(), Paths.Same(repo, Path.Combine(Root, "REPO")));
    }

    [Fact]
    public void Below_means_the_folder_itself_or_inside_it_not_a_name_that_starts_alike()
    {
        var repo = Path.Combine(Root, "repo");

        Assert.True(Paths.IsUnder(repo, repo));
        Assert.True(Paths.IsUnder(Path.Combine(repo, "src", "a.cs"), repo));
        Assert.False(Paths.IsUnder(Path.Combine(Root, "repo2"), repo));
        Assert.False(Paths.IsUnder(Root, repo));
    }
}
