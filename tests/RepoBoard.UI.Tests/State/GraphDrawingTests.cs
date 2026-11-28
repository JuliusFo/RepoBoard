using RepoBoard.Core.Git;
using RepoBoard.UI.State;

namespace RepoBoard.UI.Tests.State;

/// <summary>How the history's graph is drawn (WP-09).</summary>
public sealed class GraphDrawingTests
{
    [Fact]
    public void The_graph_is_drawn_from_the_top_edge_through_the_commit_to_the_bottom_edge()
    {
        var row = new GraphRow(1, [new(1, 1, GraphLineKind.In), new(0, 0, GraphLineKind.Through), new(1, 7, GraphLineKind.Out)], 8);

        var segments = GraphDrawing.Segments(row).ToList();

        Assert.Equal(new GraphSegment("19", "0", "19", "14", 1), segments[0]);
        Assert.Equal(new GraphSegment("7", "0", "7", "100%", 0), segments[1]);
        Assert.Equal(new GraphSegment("19", "14", "91", "100%", 1), segments[2]); // lane 7: colour 7 % 6
    }

    [Fact]
    public void Every_row_is_as_wide_as_the_widest_up_to_the_limit()
    {
        Assert.Equal(0, GraphDrawing.Lanes([]));
        Assert.Equal(3, GraphDrawing.Lanes([new GraphRow(0, [], 1), new GraphRow(2, [], 3)]));
        Assert.Equal(GraphDrawing.MaxLanes, GraphDrawing.Lanes([new GraphRow(0, [], 20)]));
        Assert.Equal(2 * GraphDrawing.Inset, GraphDrawing.Width(1));
    }
}
