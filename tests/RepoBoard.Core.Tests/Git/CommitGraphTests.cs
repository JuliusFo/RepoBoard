using RepoBoard.Core.Git;

namespace RepoBoard.Core.Tests.Git;

/// <summary>The lanes of the history graph (WP-09, ADR 0013), from commits in <c>--topo-order</c>.</summary>
public sealed class CommitGraphTests
{
    private static CommitInfo C(string id, params string[] parents) => new(id, id, parents, "Jo", default, "", id);

    private static GraphLine In(int from, int to) => new(from, to, GraphLineKind.In);
    private static GraphLine Out(int from, int to) => new(from, to, GraphLineKind.Out);
    private static GraphLine Through(int lane) => new(lane, lane, GraphLineKind.Through);

    [Fact]
    public void A_straight_history_is_one_lane()
    {
        var rows = CommitGraph.Build([C("c", "b"), C("b", "a"), C("a")]);

        Assert.All(rows, r => Assert.Equal((0, 1), (r.Column, r.Width)));
        Assert.Equal([Out(0, 0)], rows[0].Lines);
        Assert.Equal([In(0, 0), Out(0, 0)], rows[1].Lines);
        Assert.Equal([In(0, 0)], rows[2].Lines); // the root ends its lane
    }

    [Fact]
    public void A_merge_opens_a_lane_for_its_second_parent_that_ends_where_they_forked()
    {
        // m merges f into main; f and b both come from a.
        var rows = CommitGraph.Build([C("m", "b", "f"), C("f", "a"), C("b", "a"), C("a")]);

        Assert.Equal([Out(0, 0), Out(0, 1)], rows[0].Lines);
        Assert.Equal((1, 2), (rows[1].Column, rows[1].Width));
        Assert.Equal([Through(0), In(1, 1), Out(1, 1)], rows[1].Lines);
        // b's first parent a is awaited in lane 1 too: that lane moves over, main stays on the left.
        Assert.Equal(0, rows[2].Column);
        Assert.Equal([In(0, 0), new GraphLine(1, 0, GraphLineKind.Through), Out(0, 0)], rows[2].Lines);
        Assert.Equal((0, 1), (rows[3].Column, rows[3].Width));
        Assert.Equal([In(0, 0)], rows[3].Lines);
    }

    [Fact]
    public void A_second_tip_gets_its_own_lane_and_joins_the_first_at_their_common_parent()
    {
        // All branches: "feature" (f) is not in HEAD's history above b.
        var rows = CommitGraph.Build([C("h", "b"), C("f", "b"), C("b")]);

        Assert.Equal(0, rows[0].Column);
        Assert.Equal(1, rows[1].Column);
        Assert.Equal([Through(0), Out(1, 0)], rows[1].Lines); // b is awaited in lane 0 already: f's line goes there
        Assert.Equal([In(0, 0)], rows[2].Lines);
    }

    [Fact]
    public void A_free_lane_is_used_again_instead_of_growing_the_graph()
    {
        // x ends (root) in lane 1; the next tip y takes lane 1 again.
        var rows = CommitGraph.Build([C("a", "b"), C("x"), C("b", "c"), C("y", "c"), C("c")]);

        Assert.Equal(1, rows[1].Column);
        Assert.Equal(1, rows[3].Column);
        Assert.All(rows, r => Assert.True(r.Width <= 2));
    }

    [Fact]
    public void A_line_between_two_lanes_takes_the_colour_of_the_branch_on_the_right()
    {
        Assert.Equal(1, Out(0, 1).Lane); // a merge's second parent
        Assert.Equal(1, Out(1, 0).Lane); // a tip joining its fork point (E2E WP-09: was main's colour)
        Assert.Equal(1, In(1, 0).Lane);
        Assert.Equal(1, new GraphLine(1, 0, GraphLineKind.Through).Lane);
        Assert.Equal(2, Through(2).Lane);
    }

    [Fact]
    public void Parents_beyond_the_loaded_part_keep_their_lanes_open()
    {
        var rows = CommitGraph.Build([C("m", "b", "f")]);

        Assert.Equal([Out(0, 0), Out(0, 1)], rows[0].Lines);
        Assert.Equal(2, rows[0].Width);
    }
}
