namespace RepoBoard.Core.Git;

public enum GraphLineKind
{
    /// <summary>A lane that passes the row from the top edge to the bottom edge – straight, or over into a lane on its left.</summary>
    Through,

    /// <summary>From the top edge of a lane into the row's commit (its child above, or a branch that ends here).</summary>
    In,

    /// <summary>From the row's commit to the bottom edge of a lane (towards one of its parents).</summary>
    Out,
}

/// <summary>One line inside a row of the graph, between lane columns.</summary>
/// <param name="From">The lane at the top (<see cref="GraphLineKind.Through"/>, <see cref="GraphLineKind.In"/>) or the commit's column.</param>
/// <param name="To">The lane at the bottom (<see cref="GraphLineKind.Through"/>, <see cref="GraphLineKind.Out"/>) or the commit's column.</param>
public readonly record struct GraphLine(int From, int To, GraphLineKind Kind)
{
    /// <summary>
    /// The lane whose colour the line takes: of two, always the right one – the branch that joins or leaves the lane on its
    /// left (a merge's second parent, a branch's tip joining its fork point, a lane moving over).
    /// </summary>
    public int Lane => Math.Max(From, To);
}

/// <summary>The graph in one row of the history: the commit's column and the lines through the row.</summary>
/// <param name="Width">Columns the row needs (lines and the commit).</param>
public sealed record GraphRow(int Column, IReadOnlyList<GraphLine> Lines, int Width);

/// <summary>
/// Lays out the history as a graph (WP-09, ADR 0013), like <c>git log --graph</c> but as data for a drawing: each commit
/// gets a lane (column); a lane waits for the commit it leads to. The commits must come children first
/// (<c>--topo-order</c>). Lanes do not move sideways: a lane that ends leaves a gap the next branch reuses.
/// </summary>
public static class CommitGraph
{
    public static IReadOnlyList<GraphRow> Build(IReadOnlyList<CommitInfo> commits)
    {
        // The commit id each lane waits for; null for a free lane.
        var lanes = new List<string?>();
        var rows = new List<GraphRow>(commits.Count);
        foreach (var commit in commits)
        {
            var lines = new List<GraphLine>();
            var column = lanes.IndexOf(commit.Id);
            if (column < 0)
            {
                // Nothing above leads here: the tip of a branch.
                column = Free(lanes, -1);
            }

            for (var lane = 0; lane < lanes.Count; lane++)
            {
                if (lanes[lane] is not { } waiting)
                {
                    continue;
                }

                if (waiting == commit.Id)
                {
                    // Every lane that waits for this commit ends in it (branches that forked here).
                    lines.Add(new GraphLine(lane, column, GraphLineKind.In));
                    lanes[lane] = null;
                }
                else
                {
                    lines.Add(new GraphLine(lane, lane, GraphLineKind.Through));
                }
            }

            for (var p = 0; p < commit.Parents.Count; p++)
            {
                var parent = commit.Parents[p];
                var target = lanes.IndexOf(parent);
                if (p == 0 && target > column && lanes[column] is null)
                {
                    // A lane to the right waits for the same parent (a branch that forked there): it moves over into this
                    // lane, so the line of the first parents stays on the left, as in git's graph.
                    var through = lines.IndexOf(new GraphLine(target, target, GraphLineKind.Through));
                    lines[through] = new GraphLine(target, column, GraphLineKind.Through);
                    lanes[target] = null;
                    target = -1;
                }

                if (target < 0)
                {
                    // The first parent continues the commit's lane; another one opens a lane of its own.
                    target = p == 0 && lanes[column] is null ? column : Free(lanes, column);
                    lanes[target] = parent;
                }

                lines.Add(new GraphLine(column, target, GraphLineKind.Out));
            }

            while (lanes.Count > 0 && lanes[^1] is null)
            {
                lanes.RemoveAt(lanes.Count - 1);
            }

            var width = lines.Select(l => Math.Max(l.From, l.To)).Append(column).Max() + 1;
            rows.Add(new GraphRow(column, lines, width));
        }

        return rows;
    }

    /// <summary>The first free lane other than <paramref name="except"/>, a new one at the end if there is none.</summary>
    private static int Free(List<string?> lanes, int except)
    {
        for (var lane = 0; lane < lanes.Count; lane++)
        {
            if (lane != except && lanes[lane] is null)
            {
                return lane;
            }
        }

        lanes.Add(null);
        return lanes.Count - 1;
    }
}
