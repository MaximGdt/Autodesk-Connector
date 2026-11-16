using System.ComponentModel;
using AutocadMcp.Core;
using AutocadMcp.Core.Model;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AutocadMcp.Server.Tools;

/// <summary>
/// The tools Claude sees. Each one only forwards to the plugin, which validates and executes.
/// Names MUST come from Core/ToolNames (checked by SecurityTests). Descriptions are written
/// for Claude: they say when to use the tool and what the limits are.
/// </summary>
[McpServerToolType]
public sealed class AutocadTools(PipeClient client)
{
    [McpServerTool(Name = ToolNames.Status, ReadOnly = true, Destructive = false, OpenWorld = false, Title = "AutoCAD: status")]
    [Description("Check the connection to AutoCAD and read basic facts about the open drawing: name, units (INSUNITS), current layer, output folder and limits. Call this first in every session and before drawing, to learn the units.")]
    public Task<string> AutocadStatus(CancellationToken ct) => Call(ToolNames.Status, null, ct);

    [McpServerTool(Name = ToolNames.ListLayers, ReadOnly = true, Destructive = false, OpenWorld = false, Title = "AutoCAD: list layers")]
    [Description("List the layers of the open drawing with color, lineweight and on/frozen/locked state. Use before editing an existing drawing so new geometry follows its layer conventions.")]
    public Task<string> ListLayers(CancellationToken ct) => Call(ToolNames.ListLayers, null, ct);

    [McpServerTool(Name = ToolNames.CreateLayer, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, Title = "AutoCAD: create layer")]
    [Description("Create a layer, or update color/lineweight of an existing one. Locked layers are refused. To create several layers together with geometry, prefer build_drawing.")]
    public Task<string> CreateLayer(
        [Description("The layer to create or update.")] LayerSpec layer,
        CancellationToken ct) => Call(ToolNames.CreateLayer, layer, ct);

    [McpServerTool(Name = ToolNames.BuildDrawing, ReadOnly = false, Destructive = false, OpenWorld = false, Title = "AutoCAD: draw")]
    [Description("Draw layers and entities (line, polyline, circle, arc, text) in model space of the open drawing. The whole batch is validated first and applied as ONE transaction: if anything is invalid nothing is drawn and the answer lists every problem. Put a whole drawing (or a large part of it) in one call rather than many small calls. Coordinates are in drawing units: check units with autocad_status. Returns the handles of created entities.")]
    public Task<string> BuildDrawing(
        [Description("Layers and entities to draw.")] DrawingSpec spec,
        CancellationToken ct) => Call(ToolNames.BuildDrawing, spec, ct);

    [McpServerTool(Name = ToolNames.SaveDwg, ReadOnly = false, Destructive = true, OpenWorld = false, Title = "AutoCAD: save DWG copy")]
    [Description("Save a copy of the open drawing as a DWG file into the connector's output folder (shown by autocad_status). Pass only a file name, never a folder. Existing files are not replaced unless overwrite is true; ask the user before overwriting.")]
    public Task<string> SaveDwg(
        [Description("File name only, e.g. \"plan-room-101.dwg\".")] string fileName,
        [Description("DWG format version: 2018 (default), 2013, 2010, 2007, 2004 or 2000.")] string? version = null,
        [Description("Replace an existing file with the same name. Default false.")] bool overwrite = false,
        CancellationToken ct = default) =>
        Call(ToolNames.SaveDwg, new { fileName, version, overwrite }, ct);

    private async Task<string> Call(string tool, object? args, CancellationToken ct)
    {
        try
        {
            var result = await client.CallAsync(tool, args, ct).ConfigureAwait(false);
            return result.GetRawText();
        }
        catch (ConnectorException ex)
        {
            // McpException text is shown to Claude; it explains what went wrong and what to do.
            throw new McpException($"[{ex.Code}] {ex.Message}");
        }
    }
}
