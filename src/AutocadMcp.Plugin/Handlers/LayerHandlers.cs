using System.Text.Json;
using AutocadMcp.Core.Model;
using AutocadMcp.Core.Protocol;
using Autodesk.AutoCAD.DatabaseServices;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using AcColorMethod = Autodesk.AutoCAD.Colors.ColorMethod;

namespace AutocadMcp.Plugin.Handlers;

/// <summary>list_layers (read-only) and create_layer (create or update one layer).</summary>
internal static class LayerHandlers
{
    private const int MaxListed = 1000;

    public static object List(JsonElement _, ToolContext ctx)
    {
        var db = ctx.Document.Database;
        var layers = new List<object>();
        var total = 0;

        using (var tr = db.TransactionManager.StartOpenCloseTransaction())
        {
            var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId id in table)
            {
                total++;
                if (layers.Count >= MaxListed)
                {
                    continue;
                }

                var layer = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                layers.Add(new
                {
                    name = layer.Name,
                    color = layer.Color.ColorIndex,
                    lineWeight = (int)layer.LineWeight,
                    isOff = layer.IsOff,
                    isFrozen = layer.IsFrozen,
                    isLocked = layer.IsLocked,
                    isCurrent = id == db.Clayer,
                });
            }

            tr.Commit();
        }

        return new { total, truncated = total > layers.Count, layers };
    }

    public static object Create(JsonElement args, ToolContext ctx)
    {
        var spec = Args.Parse<LayerSpec>(args);
        var errors = new List<string>();
        SpecValidator.ValidateLayer(spec, "layer", errors);
        if (errors.Count > 0)
        {
            throw Args.Invalid(errors);
        }

        var db = ctx.Document.Database;
        bool created;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            created = EnsureLayer(tr, db, spec);
            tr.Commit();
        }

        return new { name = spec.Name, created, updated = !created };
    }

    /// <summary>Creates the layer or updates its color/lineweight. Returns true when it was new.</summary>
    internal static bool EnsureLayer(Transaction tr, Database db, LayerSpec spec)
    {
        var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        LayerTableRecord layer;
        var created = false;

        if (table.Has(spec.Name))
        {
            layer = (LayerTableRecord)tr.GetObject(table[spec.Name], OpenMode.ForWrite);
            if (layer.IsLocked)
            {
                throw new HandlerException(ErrorCodes.ValidationFailed, $"Слой «{spec.Name}» заблокирован. Разблокируй его в AutoCAD, если его можно менять.");
            }
        }
        else
        {
            layer = new LayerTableRecord { Name = spec.Name };
            table.UpgradeOpen();
            table.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);
            created = true;
        }

        if (spec.Color is { } color)
        {
            layer.Color = AcColor.FromColorIndex(AcColorMethod.ByAci, (short)color);
        }

        if (spec.LineWeight is { } lw)
        {
            layer.LineWeight = (LineWeight)lw;
        }

        return created;
    }
}
