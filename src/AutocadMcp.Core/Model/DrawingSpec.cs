using System.ComponentModel;

namespace AutocadMcp.Core.Model;

// These types are the public contract of build_drawing / create_layer.
// The MCP server turns them into the JSON schema Claude sees, so the [Description]
// texts are instructions for Claude: keep them precise.

[Description("A 2D point in drawing units (see units in autocad_status).")]
public sealed class Point2
{
    [Description("X coordinate.")]
    public double X { get; set; }

    [Description("Y coordinate.")]
    public double Y { get; set; }

    public Point2() { }

    public Point2(double x, double y) { X = x; Y = y; }
}

[Description("Layer to create or update before drawing entities.")]
public sealed class LayerSpec
{
    [Description("Layer name, 1-255 chars, none of < > / \\ \" : ; ? * | , = `")]
    public string Name { get; set; } = "";

    [Description("AutoCAD Color Index 1-255 (1 red, 2 yellow, 3 green, 4 cyan, 5 blue, 6 magenta, 7 white/black). Omit for default 7.")]
    public int? Color { get; set; }

    [Description("Lineweight in hundredths of a millimetre, one of: 0,5,9,13,15,18,20,25,30,35,40,50,53,60,70,80,90,100,106,120,140,158,200,211. Omit for default.")]
    public int? LineWeight { get; set; }
}

[Description("One entity to draw. Fill only the fields that apply to its type.")]
public sealed class EntitySpec
{
    [Description("Entity type: line | polyline | circle | arc | text")]
    public string Type { get; set; } = "";

    [Description("Target layer. Created automatically if it does not exist. Omit for the current layer.")]
    public string? Layer { get; set; }

    [Description("Color override, ACI 1-255. Omit to use the layer color (ByLayer).")]
    public int? Color { get; set; }

    [Description("line: exactly 2 points. polyline: 2 or more points.")]
    public List<Point2>? Points { get; set; }

    [Description("polyline: true to close the shape.")]
    public bool? Closed { get; set; }

    [Description("circle, arc: centre point.")]
    public Point2? Center { get; set; }

    [Description("circle, arc: radius > 0.")]
    public double? Radius { get; set; }

    [Description("arc: start angle in degrees, counter-clockwise from +X.")]
    public double? StartAngle { get; set; }

    [Description("arc: end angle in degrees, counter-clockwise from +X.")]
    public double? EndAngle { get; set; }

    [Description("text: single-line content, no line breaks.")]
    public string? Text { get; set; }

    [Description("text: insertion point (left baseline).")]
    public Point2? Position { get; set; }

    [Description("text: height in drawing units, > 0.")]
    public double? Height { get; set; }

    [Description("text: rotation in degrees. Omit for 0.")]
    public double? Rotation { get; set; }
}

[Description("A batch of layers and entities applied to the open drawing as ONE transaction (one Ctrl+Z).")]
public sealed class DrawingSpec
{
    [Description("Layers to create first (optional).")]
    public List<LayerSpec>? Layers { get; set; }

    [Description("Entities to draw, in order.")]
    public List<EntitySpec> Entities { get; set; } = [];
}

public static class EntityTypes
{
    public const string Line = "line";
    public const string Polyline = "polyline";
    public const string Circle = "circle";
    public const string Arc = "arc";
    public const string Text = "text";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Line, Polyline, Circle, Arc, Text,
    };
}
