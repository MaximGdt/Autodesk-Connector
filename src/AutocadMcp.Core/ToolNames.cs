namespace AutocadMcp.Core;

/// <summary>
/// The complete allowlist of actions Claude can ask AutoCAD to perform.
/// Nothing outside this list is ever executed: the plugin router rejects unknown names,
/// and tests/AutocadMcp.SecurityTests checks that the MCP server exposes only these.
/// Adding a name = adding a capability, so it must follow the recipe in CLAUDE.md
/// and be described in docs/TOOLS.md.
/// </summary>
public static class ToolNames
{
    // Read-only
    public const string Status = "autocad_status";
    public const string ListLayers = "list_layers";

    // Changes the drawing (always inside one transaction, undoable with Ctrl+Z)
    public const string CreateLayer = "create_layer";
    public const string BuildDrawing = "build_drawing";

    // Writes a file (only into the allowed output folder, see PathGuard)
    public const string SaveDwg = "save_dwg";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Status, ListLayers, CreateLayer, BuildDrawing, SaveDwg,
    };
}
