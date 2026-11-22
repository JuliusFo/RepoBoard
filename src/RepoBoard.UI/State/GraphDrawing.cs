using System.Globalization;
using RepoBoard.Core.Git;

namespace RepoBoard.UI.State;

/// <summary>One line of the graph in a row's SVG; y as an SVG length (pixels, or a percentage of the row's height).</summary>
/// <param name="Colour">Index of the lane colour (CSS class <c>g0</c> … <c>g5</c>).</param>
public readonly record struct GraphSegment(string X1, string Y1, string X2, string Y2, int Colour);

/// <summary>
/// Draws a row of <see cref="CommitGraph"/> (WP-09, ADR 0013): each row is an SVG as high as the row, so its lines meet the
/// next row's without knowing its height – the commit sits at a fixed height beside the first text line, a line to a
/// parent goes to the bottom edge (<c>100%</c>). At most <see cref="MaxLanes"/> lanes are drawn; the rest is cut off.
/// </summary>
public static class GraphDrawing
{
    public const int MaxLanes = 8;
    public const int Colours = 6;

    /// <summary>Pixels between two lanes, and from the left edge to the middle of the first.</summary>
    public const int Step = 12;
    public const int Inset = 7;

    /// <summary>The commit's height in the row: the middle of the first text line.</summary>
    public const int NodeY = 14;

    public const string Bottom = "100%";

    /// <summary>The lanes the history needs, capped: every row's SVG has this width so the text beside it lines up.</summary>
    public static int Lanes(IReadOnlyList<GraphRow> rows) => rows.Count == 0 ? 0 : Math.Min(rows.Max(r => r.Width), MaxLanes);

    public static int Width(int lanes) => lanes == 0 ? 0 : (lanes - 1) * Step + 2 * Inset;

    public static string X(int lane) => (Inset + lane * Step).ToString(CultureInfo.InvariantCulture);

    public static int Colour(int lane) => lane % Colours;

    public static IEnumerable<GraphSegment> Segments(GraphRow row)
    {
        var node = NodeY.ToString(CultureInfo.InvariantCulture);
        foreach (var line in row.Lines)
        {
            yield return line.Kind switch
            {
                GraphLineKind.Through => new GraphSegment(X(line.From), "0", X(line.To), Bottom, Colour(line.Lane)),
                GraphLineKind.In => new GraphSegment(X(line.From), "0", X(line.To), node, Colour(line.Lane)),
                _ => new GraphSegment(X(line.From), node, X(line.To), Bottom, Colour(line.Lane)),
            };
        }
    }
}
