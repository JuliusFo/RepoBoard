using RepoBoard.UI.State;

namespace RepoBoard.UI.Tests.State;

/// <summary>How the page writes commits, refs, paths and branches (review 0.4.0, B7).</summary>
public sealed class CommitTextTests
{
    [Fact]
    public void Refs_read_as_names_without_head_s_arrow() =>
        Assert.Equal(["main", "origin/main", "tag: v1.0"], CommitText.RefNames("HEAD -> main, origin/main, tag: v1.0"));

    [Theory]
    [InlineData("0123456789abcdef", "0123456")]
    [InlineData("abc", "abc")]
    [InlineData(null, null)]
    public void A_commit_id_is_cut_to_seven(string? id, string? expected) => Assert.Equal(expected, CommitText.Short(id));

    [Fact]
    public void A_renamed_file_names_where_it_came_from()
    {
        Assert.Equal("old.cs → new.cs", CommitText.PathTitle("new.cs", "old.cs"));
        Assert.Equal("a.cs", CommitText.PathTitle("a.cs", null));
        Assert.Equal("rechnung⟨U+202E⟩txt.exe", CommitText.PathTitle("rechnung‮txt.exe", null)); // second review pass R1
    }

    [Theory]
    [InlineData("main", true)]
    [InlineData("develop", true)]
    [InlineData("feature/main", false)]
    [InlineData(null, false)]
    public void The_main_lines_are_named(string? branch, bool main) => Assert.Equal(main, CommitText.IsMainBranch(branch));
}
