using System.Diagnostics;
using System.Text.Json;
using AutocadMcp.Core;
using AutocadMcp.Core.Model;
using AutocadMcp.Core.Protocol;

namespace AutocadMcp.Server.Smoke;

/// <summary>
/// End-to-end check against a REAL running AutoCAD, without Claude:
///   autocad-mcp-server.exe --smoke
/// It draws on layer MCP-SMOKE in a NEW EMPTY drawing, checks speed budgets and
/// that dangerous requests are refused, then prints a PASS/FAIL table in Russian.
/// Budgets are the numbers in docs/ACCEPTANCE.md; keep the two in sync.
/// </summary>
public static class SmokeTest
{
    private sealed record Step(string Name, bool Pass, long Ms, long BudgetMs, string Note);

    public static async Task<int> RunAsync(PipeClient client)
    {
        var steps = new List<Step>();
        Console.WriteLine("Самопроверка коннектора AutoCAD MCP");
        Console.WriteLine("Нужно: запущен AutoCAD 2025, открыт НОВЫЙ ПУСТОЙ чертёж.\n");

        // 1. Connection and safety check of the target drawing.
        var (ok, ms, result, note) = await Try(client, ToolNames.Status, null);
        var drawingName = ok && result.TryGetProperty("drawing", out var d) && d.ValueKind == JsonValueKind.Object
            ? d.GetProperty("name").GetString() ?? ""
            : "";
        steps.Add(new Step("Связь с AutoCAD", ok && drawingName.Length > 0, ms, 500, ok ? drawingName : note));
        if (!ok || drawingName.Length == 0)
        {
            return Print(steps, "Нет связи или не открыт чертёж. Остальные проверки пропущены.");
        }

        var fileName = Path.GetFileNameWithoutExtension(drawingName);
        if (!fileName.StartsWith("Drawing", StringComparison.OrdinalIgnoreCase) &&
            !fileName.StartsWith("Чертеж", StringComparison.OrdinalIgnoreCase) &&
            !fileName.StartsWith("Чертёж", StringComparison.OrdinalIgnoreCase))
        {
            return Print(steps, $"Открыт сохранённый чертёж «{drawingName}». Самопроверка рисует тестовые объекты, поэтому запускается только в НОВОМ чертеже (Ctrl+N).");
        }

        // 2. Layer.
        (ok, ms, _, note) = await Try(client, ToolNames.CreateLayer, new LayerSpec { Name = "MCP-SMOKE", Color = 3 });
        steps.Add(new Step("Создание слоя", ok, ms, 1000, note));

        // 3. One of each entity type.
        var mixed = new DrawingSpec
        {
            Entities =
            [
                new EntitySpec { Type = "line", Layer = "MCP-SMOKE", Points = [new(0, 0), new(100, 0)] },
                new EntitySpec { Type = "polyline", Layer = "MCP-SMOKE", Closed = true, Points = [new(0, 10), new(100, 10), new(100, 60), new(0, 60)] },
                new EntitySpec { Type = "circle", Layer = "MCP-SMOKE", Center = new(50, 35), Radius = 15 },
                new EntitySpec { Type = "arc", Layer = "MCP-SMOKE", Center = new(150, 35), Radius = 20, StartAngle = 0, EndAngle = 180 },
                new EntitySpec { Type = "text", Layer = "MCP-SMOKE", Text = "Самопроверка MCP", Position = new(0, 70), Height = 5 },
            ],
        };
        (ok, ms, _, note) = await Try(client, ToolNames.BuildDrawing, mixed);
        steps.Add(new Step("Все типы объектов", ok, ms, 1000, note));

        // 4. Speed: 1000 lines in one request.
        var grid = new DrawingSpec();
        for (var i = 0; i < 1000; i++)
        {
            var y = 100 + i * 0.5;
            grid.Entities.Add(new EntitySpec { Type = "line", Layer = "MCP-SMOKE", Points = [new(0, y), new(200, y)] });
        }

        (ok, ms, _, note) = await Try(client, ToolNames.BuildDrawing, grid);
        steps.Add(new Step("1000 отрезков за один запрос", ok, ms, 3000, note));

        // 5. Read back.
        (ok, ms, result, note) = await Try(client, ToolNames.ListLayers, null);
        var hasLayer = ok && result.GetProperty("layers").EnumerateArray().Any(l => l.GetProperty("name").GetString() == "MCP-SMOKE");
        steps.Add(new Step("Чтение слоёв", hasLayer, ms, 500, hasLayer ? "" : "слой MCP-SMOKE не найден " + note));

        // 6-9. Safety: these MUST be refused.
        steps.Add(await ExpectRefusal(client, "Отказ: неизвестный тип объекта", ToolNames.BuildDrawing,
            new DrawingSpec { Entities = [new EntitySpec { Type = "explode" }] }, ErrorCodes.ValidationFailed));
        steps.Add(await ExpectRefusal(client, "Отказ: выход из папки (..\\)", ToolNames.SaveDwg,
            new { fileName = "..\\evil.dwg" }, ErrorCodes.PathDenied));
        steps.Add(await ExpectRefusal(client, "Отказ: чужая папка (C:\\Windows)", ToolNames.SaveDwg,
            new { fileName = "C:\\Windows\\evil.dwg" }, ErrorCodes.PathDenied));
        steps.Add(await ExpectRefusal(client, "Отказ: неразрешённое действие", "run_lisp",
            new { code = "(command \"_.QUIT\")" }, ErrorCodes.UnknownTool));

        // 10. Save a copy into the allowed folder.
        (ok, ms, result, note) = await Try(client, ToolNames.SaveDwg, new { fileName = "mcp-smoke-test.dwg", overwrite = true });
        var saved = ok && File.Exists(result.GetProperty("path").GetString());
        steps.Add(new Step("Сохранение DWG в разрешённую папку", saved, ms, 5000, ok ? result.GetProperty("path").GetString() ?? "" : note));

        return Print(steps, "Тестовые объекты лежат на слое MCP-SMOKE. Закрой этот чертёж без сохранения.");
    }

    private static async Task<(bool Ok, long Ms, JsonElement Result, string Note)> Try(PipeClient client, string tool, object? args)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await client.CallAsync(tool, args);
            return (true, sw.ElapsedMilliseconds, result, "");
        }
        catch (ConnectorException ex)
        {
            return (false, sw.ElapsedMilliseconds, default, $"[{ex.Code}] {ex.Message}");
        }
    }

    private static async Task<Step> ExpectRefusal(PipeClient client, string name, string tool, object args, string expectedCode)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await client.CallAsync(tool, args);
            return new Step(name, false, sw.ElapsedMilliseconds, 1000, "ОПАСНО: запрос выполнился, а должен был быть отклонён");
        }
        catch (ConnectorException ex)
        {
            var pass = ex.Code == expectedCode;
            return new Step(name, pass, sw.ElapsedMilliseconds, 1000, pass ? "отклонён, как и должен" : $"ожидался {expectedCode}, получен [{ex.Code}] {ex.Message}");
        }
    }

    private static int Print(List<Step> steps, string footer)
    {
        Console.WriteLine($"{"Проверка",-40} {"Итог",-6} {"мс",6} {"лимит",6}  Примечание");
        Console.WriteLine(new string('-', 100));
        foreach (var s in steps)
        {
            var slow = s.Pass && s.Ms > s.BudgetMs;
            var verdict = !s.Pass ? "ОШИБКА" : slow ? "МЕДЛ." : "OK";
            Console.WriteLine($"{s.Name,-40} {verdict,-6} {s.Ms,6} {s.BudgetMs,6}  {s.Note}");
        }

        var failed = steps.Count(s => !s.Pass);
        var slowCount = steps.Count(s => s.Pass && s.Ms > s.BudgetMs);
        Console.WriteLine(new string('-', 100));
        Console.WriteLine(failed == 0 && slowCount == 0
            ? $"ИТОГ: ВСЁ В ПОРЯДКЕ ({steps.Count} проверок)."
            : $"ИТОГ: ошибок {failed}, медленных {slowCount}. Покажи эту таблицу агенту.");
        Console.WriteLine(footer);
        return failed == 0 && slowCount == 0 ? 0 : 1;
    }
}
