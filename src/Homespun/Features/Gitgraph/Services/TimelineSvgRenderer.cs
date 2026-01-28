using System.Globalization;
using System.Text;

namespace Homespun.Features.Gitgraph.Services;

/// <summary>
/// Helper class for generating SVG paths and elements for timeline rendering.
/// </summary>
public static class TimelineSvgRenderer
{
    /// <summary>
    /// Width of each lane in pixels.
    /// </summary>
    public const int LaneWidth = 24;

    /// <summary>
    /// Height of each row in pixels.
    /// </summary>
    public const int RowHeight = 40;

    /// <summary>
    /// Radius of node circles (PRs).
    /// </summary>
    public const int NodeRadius = 6;

    /// <summary>
    /// Size of diamond nodes (issues) - half-width/height.
    /// </summary>
    public const int DiamondSize = 7;

    /// <summary>
    /// Stroke width for lane lines.
    /// </summary>
    public const double LineStrokeWidth = 2;

    /// <summary>
    /// Calculate the SVG width needed for the given number of lanes.
    /// </summary>
    public static int CalculateSvgWidth(int maxLanes)
    {
        return LaneWidth * Math.Max(maxLanes, 1) + LaneWidth / 2;
    }

    /// <summary>
    /// Get the X coordinate for the center of a lane.
    /// </summary>
    public static int GetLaneCenterX(int laneIndex)
    {
        return LaneWidth / 2 + laneIndex * LaneWidth;
    }

    /// <summary>
    /// Get the Y coordinate for the center of a row.
    /// </summary>
    public static int GetRowCenterY()
    {
        return RowHeight / 2;
    }

    /// <summary>
    /// Generate an SVG path for a vertical lane line segment.
    /// </summary>
    /// <param name="laneIndex">The lane index.</param>
    /// <param name="skipMiddle">If true, skip the middle section where a node would be.</param>
    /// <param name="hasNodeInLane">True if this lane has a node in this row.</param>
    public static string GenerateVerticalLine(int laneIndex, bool hasNodeInLane)
    {
        var x = GetLaneCenterX(laneIndex);
        var centerY = GetRowCenterY();

        if (hasNodeInLane)
        {
            // Draw line segments above and below the node
            var topSegment = $"M {x} 0 L {x} {centerY - NodeRadius - 2}";
            var bottomSegment = $"M {x} {centerY + NodeRadius + 2} L {x} {RowHeight}";
            return $"{topSegment} {bottomSegment}";
        }

        // Full vertical line through the row
        return $"M {x} 0 L {x} {RowHeight}";
    }

    /// <summary>
    /// Generate an SVG path for a connector from one lane to another.
    /// Creates an L-shaped bend for lane switching.
    /// </summary>
    /// <param name="fromLane">Source lane index.</param>
    /// <param name="toLane">Target lane index.</param>
    public static string GenerateConnector(int fromLane, int toLane)
    {
        var fromX = GetLaneCenterX(fromLane);
        var toX = GetLaneCenterX(toLane);
        var centerY = GetRowCenterY();

        // L-shaped connector: vertical from top to middle, horizontal to target, vertical to node
        var bendY = centerY - NodeRadius - 4;

        return $"M {fromX} 0 L {fromX} {bendY} L {toX} {bendY} L {toX} {centerY - NodeRadius - 2}";
    }

    /// <summary>
    /// Generate SVG for a circle node (used for PRs).
    /// </summary>
    /// <param name="laneIndex">Lane where the node is positioned.</param>
    /// <param name="color">Fill color for the node.</param>
    public static string GenerateCircleNode(int laneIndex, string color)
    {
        var cx = GetLaneCenterX(laneIndex);
        var cy = GetRowCenterY();
        return $"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{NodeRadius}\" fill=\"{EscapeAttribute(color)}\" />";
    }

    /// <summary>
    /// Generate SVG for a diamond node (used for issues).
    /// </summary>
    /// <param name="laneIndex">Lane where the node is positioned.</param>
    /// <param name="color">Fill color for the node.</param>
    public static string GenerateDiamondNode(int laneIndex, string color)
    {
        var cx = GetLaneCenterX(laneIndex);
        var cy = GetRowCenterY();
        var s = DiamondSize;

        // Diamond path: top, right, bottom, left
        var path = $"M {cx} {cy - s} L {cx + s} {cy} L {cx} {cy + s} L {cx - s} {cy} Z";
        return $"<path d=\"{path}\" fill=\"{EscapeAttribute(color)}\" />";
    }

    /// <summary>
    /// Generate SVG for a "load more" button node.
    /// </summary>
    /// <param name="laneIndex">Lane where the node is positioned.</param>
    /// <param name="color">Color for the node.</param>
    public static string GenerateLoadMoreNode(int laneIndex, string color)
    {
        var cx = GetLaneCenterX(laneIndex);
        var cy = GetRowCenterY();
        var r = NodeRadius + 2;

        var sb = new StringBuilder();
        sb.Append($"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{r}\" fill=\"{EscapeAttribute(color)}\" stroke=\"white\" stroke-width=\"2\" />");
        sb.Append($"<text x=\"{cx}\" y=\"{cy}\" text-anchor=\"middle\" dominant-baseline=\"central\" fill=\"white\" font-size=\"14\" font-weight=\"bold\">+</text>");
        return sb.ToString();
    }

    /// <summary>
    /// Generate the complete SVG element for a row's graph cell.
    /// </summary>
    /// <param name="nodeLane">Lane containing the node.</param>
    /// <param name="activeLanes">Set of lanes with active lines.</param>
    /// <param name="connectorFromLane">Source lane for connector, if any.</param>
    /// <param name="maxLanes">Maximum number of lanes (determines width).</param>
    /// <param name="nodeColor">Color for the node.</param>
    /// <param name="isIssue">True for diamond shape, false for circle.</param>
    /// <param name="isLoadMore">True for load more button style.</param>
    /// <param name="laneColors">Colors for each lane line.</param>
    public static string GenerateRowSvg(
        int nodeLane,
        IReadOnlySet<int> activeLanes,
        int? connectorFromLane,
        int maxLanes,
        string nodeColor,
        bool isIssue,
        bool isLoadMore,
        IReadOnlyDictionary<int, string>? laneColors = null)
    {
        var width = CalculateSvgWidth(maxLanes);
        var sb = new StringBuilder();

        sb.Append($"<svg width=\"{width}\" height=\"{RowHeight}\" xmlns=\"http://www.w3.org/2000/svg\">");

        // Draw vertical lane lines for all active lanes
        foreach (var lane in activeLanes.OrderBy(l => l))
        {
            var hasNode = lane == nodeLane;
            var lineColor = laneColors?.GetValueOrDefault(lane) ?? "#6b7280";
            var linePath = GenerateVerticalLine(lane, hasNode);
            sb.Append($"<path d=\"{linePath}\" stroke=\"{EscapeAttribute(lineColor)}\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");
        }

        // Draw connector if coming from a different lane
        if (connectorFromLane.HasValue && connectorFromLane.Value != nodeLane)
        {
            var connectorPath = GenerateConnector(connectorFromLane.Value, nodeLane);
            sb.Append($"<path d=\"{connectorPath}\" stroke=\"{EscapeAttribute(nodeColor)}\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");
        }

        // Draw the node
        if (isLoadMore)
        {
            sb.Append(GenerateLoadMoreNode(nodeLane, nodeColor));
        }
        else if (isIssue)
        {
            sb.Append(GenerateDiamondNode(nodeLane, nodeColor));
        }
        else
        {
            sb.Append(GenerateCircleNode(nodeLane, nodeColor));
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>
    /// Escape a string for use in an XML/SVG attribute.
    /// </summary>
    private static string EscapeAttribute(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}
