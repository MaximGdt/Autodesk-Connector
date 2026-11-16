using System.Text.Json;
using AutocadMcp.Core.Model;
using AutocadMcp.Core.Protocol;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using AcColorMethod = Autodesk.AutoCAD.Colors.ColorMethod;

namespace AutocadMcp.Plugin.Handlers;

/// <summary>
/// build_drawing: the main drawing tool. The whole spec is validated first, then applied
/// in ONE transaction. Either everything appears or nothing does.
///
/// HOW TO ADD AN ENTITY TYPE (stage 3 in PLAN.md):
///   1. Core: add the type to EntityTypes + fields to EntitySpec + rules in SpecValidator + unit tests.
///   2. Here: add a case to CreateEntity.
///   3. docs/TOOLS.md + an acceptance scenario in docs/ACCEPTANCE.md.
/// </summary>
internal static class BuildDrawingHandler
{
    private const int MaxReturnedHandles = 500;

    public static object Handle(JsonElement args, ToolContext ctx)
    {
        var spec = Args.Parse<DrawingSpec>(args);
        var errors = SpecValidator.Validate(spec, ctx.Limits);
        if (errors.Count > 0)
        {
            throw Args.Invalid(errors);
        }

        var db = ctx.Document.Database;
        var handles = new List<string>(Math.Min(spec.Entities.Count, MaxReturnedHandles));
        var createdLayers = new List<string>();

        using (var tr = db.TransactionManager.StartTransaction())
        {
            foreach (var layer in spec.Layers ?? [])
            {
                if (LayerHandlers.EnsureLayer(tr, db, layer))
                {
                    createdLayers.Add(layer.Name);
                }
            }

            var checkedLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in spec.Entities)
            {
                if (e.Layer is { } name && checkedLayers.Add(name) &&
                    LayerHandlers.EnsureLayer(tr, db, new LayerSpec { Name = name }))
                {
                    createdLayers.Add(name);
                }
            }

            var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

            foreach (var e in spec.Entities)
            {
                var entity = CreateEntity(e);
                entity.SetDatabaseDefaults(db);
                if (e.Layer is { } layerName)
                {
                    entity.Layer = layerName;
                }

                if (e.Color is { } color)
                {
                    entity.Color = AcColor.FromColorIndex(AcColorMethod.ByAci, (short)color);
                }

                modelSpace.AppendEntity(entity);
                tr.AddNewlyCreatedDBObject(entity, true);

                if (handles.Count < MaxReturnedHandles)
                {
                    handles.Add(entity.Handle.ToString());
                }
            }

            tr.Commit();
        }

        return new
        {
            created = spec.Entities.Count,
            createdLayers,
            handles,
            handlesTruncated = spec.Entities.Count > handles.Count,
        };
    }

    private static Entity CreateEntity(EntitySpec e)
    {
        switch (e.Type.Trim().ToLowerInvariant())
        {
            case EntityTypes.Line:
                return new Line(P3(e.Points![0]), P3(e.Points[1]));

            case EntityTypes.Polyline:
                var pl = new Polyline(e.Points!.Count);
                for (var i = 0; i < e.Points.Count; i++)
                {
                    pl.AddVertexAt(i, new Point2d(e.Points[i].X, e.Points[i].Y), 0, 0, 0);
                }

                pl.Closed = e.Closed ?? false;
                return pl;

            case EntityTypes.Circle:
                return new Circle(P3(e.Center!), Vector3d.ZAxis, e.Radius!.Value);

            case EntityTypes.Arc:
                return new Arc(P3(e.Center!), e.Radius!.Value, Rad(e.StartAngle!.Value), Rad(e.EndAngle!.Value));

            case EntityTypes.Text:
                return new DBText
                {
                    Position = P3(e.Position!),
                    Height = e.Height!.Value,
                    TextString = e.Text!,
                    Rotation = Rad(e.Rotation ?? 0),
                };

            default:
                // Unreachable after validation; kept as a guard.
                throw new HandlerException(ErrorCodes.ValidationFailed, $"Тип «{e.Type}» не поддерживается.");
        }
    }

    private static Point3d P3(Point2 p) => new(p.X, p.Y, 0);

    private static double Rad(double degrees) => degrees * Math.PI / 180.0;
}
