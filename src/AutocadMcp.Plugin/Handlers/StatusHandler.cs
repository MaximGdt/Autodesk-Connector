using AutocadMcp.Core.Settings;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutocadMcp.Plugin.Handlers;

/// <summary>autocad_status: read-only health check. Works even with no drawing open.</summary>
internal static class StatusHandler
{
    public static object Handle(Document? doc, ConnectorSettings settings)
    {
        object? drawing = null;
        if (doc is not null)
        {
            var db = doc.Database;
            string currentLayer;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var layer = (LayerTableRecord)tr.GetObject(db.Clayer, OpenMode.ForRead);
                currentLayer = layer.Name;
                tr.Commit();
            }

            drawing = new
            {
                name = doc.Name,
                units = db.Insunits.ToString(),
                currentLayer,
                busy = !doc.Editor.IsQuiescent,
            };
        }

        return new
        {
            connected = true,
            connectorVersion = typeof(StatusHandler).Assembly.GetName().Version?.ToString(),
            autocadVersion = AcApp.Version.ToString(),
            drawing,
            outputFolder = settings.OutputFolder,
            maxEntitiesPerRequest = settings.MaxEntities,
        };
    }
}
