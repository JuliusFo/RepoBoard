using RepoBoard.Core.Git;
using RepoBoard.UI.State;

namespace RepoBoard.UI.Tests.State;

public sealed class DiffSelectionTests
{
    private static RepoStatus Status(params FileChange[] changes) => new("main", "abc", "origin/main", 0, 0, changes);

    private static FileChange Change(string path, char index, char worktree, bool untracked = false) =>
        new(path, null, index, worktree, untracked, false);

    /// <summary>
    /// The diff stays on its file while the status changes: on its side if the file is still there, on the other side
    /// after staging or unstaging, and it ends when the file is in neither list.
    /// </summary>
    [Theory]
    [InlineData(false, "a.txt:.M", false)] // unchanged
    [InlineData(false, "a.txt:MM", false)] // partly staged: stays unstaged
    [InlineData(false, "a.txt:M.", true)] // staged meanwhile: follows to the staged side
    [InlineData(true, "a.txt:.M", false)] // unstaged meanwhile
    [InlineData(true, "a.txt:MM", true)]
    [InlineData(false, "b.txt:.M", null)] // committed or reverted
    [InlineData(false, "", null)]
    public void The_diff_follows_its_file(bool staged, string status, bool? expected)
    {
        var changes = status.Length == 0 ? [] : status.Split(',').Select(s => Change(s.Split(':')[0], s.Split(':')[1][0], s.Split(':')[1][1])).ToArray();

        var resolved = DiffSelection.Resolve(new DiffTarget("a.txt", staged), Status(changes));

        Assert.Equal(expected, resolved?.Staged);
        if (resolved is { } r)
        {
            Assert.Equal("a.txt", r.Change.Path);
        }
    }

    [Fact]
    public void A_new_file_is_unstaged_until_it_is_added()
    {
        var target = new DiffTarget("n.txt", Staged: false);

        Assert.Equal(false, DiffSelection.Resolve(target, Status(Change("n.txt", '.', '?', untracked: true)))?.Staged);
        Assert.Equal(true, DiffSelection.Resolve(target, Status(Change("n.txt", 'A', '.')))?.Staged);
    }

    /// <summary>
    /// Review WP-05: the target moves to the side the diff followed to (a click on the marked file closes it, a later change
    /// does not send it back) and ends when the file is gone – a later change of the file does not reopen the diff.
    /// </summary>
    [Fact]
    public void The_target_moves_with_the_diff_and_ends_with_the_file()
    {
        var unstaged = new DiffTarget("a.txt", Staged: false);

        var followed = DiffSelection.Next(unstaged, Status(Change("a.txt", 'M', '.')));
        Assert.Equal(new DiffTarget("a.txt", Staged: true), followed);
        Assert.Equal(true, DiffSelection.Resolve(followed, Status(Change("a.txt", 'M', 'M')))?.Staged);

        var gone = DiffSelection.Next(followed, Status());
        Assert.Null(gone);
        Assert.Null(DiffSelection.Resolve(gone, Status(Change("a.txt", '.', 'M'))));

        Assert.Equal(unstaged, DiffSelection.Next(unstaged, null));
    }

    [Fact]
    public void Nothing_is_selected_without_a_target_or_a_status()
    {
        Assert.Null(DiffSelection.Resolve(null, Status(Change("a.txt", '.', 'M'))));
        Assert.Null(DiffSelection.Resolve(new DiffTarget("a.txt", false), null));
    }
}
