using System.Diagnostics;
using System.Text.Json;
using AutocadMcp.Core;
using AutocadMcp.Core.Model;
using AutocadMcp.Core.Protocol;
using AutocadMcp.Core.Settings;
using AutocadMcp.Plugin.Handlers;
using Autodesk.AutoCAD.ApplicationServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutocadMcp.Plugin;

/// <summary>What every handler gets: the active drawing (already locked) and the settings.</summary>
internal sealed record ToolContext(Document Document, ConnectorSettings Settings, Limits Limits);

/// <summary>
/// Maps tool names to handlers. Runs on AutoCAD's main thread.
/// The handler table is the plugin side of the allowlist in Core/ToolNames.cs.
/// </summary>
internal sealed class ToolRouter
{
    private readonly ConnectorSettings _settings;
    private readonly Dictionary<string, Func<JsonElement, ToolContext, object>> _handlers;

    public ToolRouter(ConnectorSettings settings)
    {
        _settings = settings;
        _handlers = new(StringComparer.Ordinal)
        {
            [ToolNames.ListLayers] = LayerHandlers.List,
            [ToolNames.CreateLayer] = LayerHandlers.Create,
            [ToolNames.BuildDrawing] = BuildDrawingHandler.Handle,
            [ToolNames.SaveDwg] = SaveDwgHandler.Handle,
        };
    }

    public bool IsKnown(string tool) => tool == ToolNames.Status || _handlers.ContainsKey(tool);

    /// <summary>Executes one request. Never throws: every failure becomes an error response.</summary>
    public PipeResponse Execute(PipeRequest request)
    {
        var sw = Stopwatch.StartNew();
        PipeResponse response;
        Document? doc = null;

        try
        {
            doc = AcApp.DocumentManager.MdiActiveDocument;

            if (request.Tool == ToolNames.Status)
            {
                response = PipeResponse.Success(request.Id, JsonWire.ToElement(StatusHandler.Handle(doc, _settings)));
            }
            else if (!_handlers.TryGetValue(request.Tool, out var handler))
            {
                response = PipeResponse.Failure(request.Id, ErrorCodes.UnknownTool, $"Неизвестное действие «{request.Tool}».");
            }
            else if (doc is null)
            {
                response = PipeResponse.Failure(request.Id, ErrorCodes.NoDocument, "В AutoCAD нет открытого чертежа. Открой или создай чертёж.");
            }
            else if (!doc.Editor.IsQuiescent)
            {
                response = PipeResponse.Failure(request.Id, ErrorCodes.Busy, "В AutoCAD сейчас выполняется команда. Заверши её (Esc) и повтори.");
            }
            else
            {
                using (doc.LockDocument())
                {
                    var result = handler(request.Args, new ToolContext(doc, _settings, _settings.ToLimits()));
                    response = PipeResponse.Success(request.Id, JsonWire.ToElement(result));
                }
            }
        }
        catch (HandlerException ex)
        {
            response = PipeResponse.Failure(request.Id, ex.Code, ex.Message);
        }
        catch (JsonException ex)
        {
            response = PipeResponse.Failure(request.Id, ErrorCodes.BadRequest, $"Некорректные параметры: {ex.Message}");
        }
        catch (System.Exception ex)
        {
            // Unexpected: report the type, keep details in the AutoCAD command line for the developer.
            response = PipeResponse.Failure(request.Id, ErrorCodes.Internal, $"Внутренняя ошибка коннектора ({ex.GetType().Name}). Подробности в командной строке AutoCAD.");
            Echo(doc, $"[Claude] ОШИБКА в {request.Tool}: {ex}");
        }

        sw.Stop();
        if (_settings.EchoToCommandLine && request.Tool != ToolNames.Status)
        {
            var outcome = response.Ok ? "OK" : $"ОТКАЗ ({response.Error?.Code}): {response.Error?.Message}";
            Echo(doc, $"[Claude] {request.Tool} → {outcome} ({sw.ElapsedMilliseconds} мс)");
        }

        return response;
    }

    private static void Echo(Document? doc, string message)
    {
        try
        {
            doc?.Editor.WriteMessage($"\n{message}\n");
        }
        catch (System.Exception)
        {
            // Echo is informational only.
        }
    }
}
