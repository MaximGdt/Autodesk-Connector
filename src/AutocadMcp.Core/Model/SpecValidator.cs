using AutocadMcp.Core.Safety;

namespace AutocadMcp.Core.Model;

/// <summary>
/// Checks every request BEFORE AutoCAD is touched. A spec either passes completely
/// or nothing is drawn, so Claude gets the full list of problems in one answer.
/// </summary>
public static class SpecValidator
{
    private static readonly HashSet<int> ValidLineWeights =
    [
        0, 5, 9, 13, 15, 18, 20, 25, 30, 35, 40, 50, 53, 60, 70, 80, 90, 100, 106, 120, 140, 158, 200, 211,
    ];

    private const int MaxReportedErrors = 25;

    public static IReadOnlyList<string> Validate(DrawingSpec? spec, Limits limits)
    {
        var errors = new List<string>();
        if (spec is null)
        {
            errors.Add("spec: пустой запрос");
            return errors;
        }

        var layers = spec.Layers ?? [];
        if (layers.Count > limits.MaxLayersPerRequest)
        {
            errors.Add($"layers: слишком много слоёв в одном запросе ({layers.Count}, максимум {limits.MaxLayersPerRequest})");
        }

        for (var i = 0; i < layers.Count && errors.Count < MaxReportedErrors; i++)
        {
            ValidateLayer(layers[i], $"layers[{i}]", errors);
        }

        var entities = spec.Entities ?? [];
        if (entities.Count == 0)
        {
            errors.Add("entities: нужен хотя бы один объект");
        }
        else if (entities.Count > limits.MaxEntities)
        {
            errors.Add($"entities: слишком много объектов ({entities.Count}, максимум {limits.MaxEntities}). Разбей чертёж на несколько вызовов.");
            return errors;
        }

        var totalVertices = 0;
        for (var i = 0; i < entities.Count && errors.Count < MaxReportedErrors; i++)
        {
            totalVertices += entities[i]?.Points?.Count ?? 0;
            ValidateEntity(entities[i], $"entities[{i}]", limits, errors);
        }

        if (totalVertices > limits.MaxTotalVertices)
        {
            errors.Add($"entities: слишком много вершин суммарно ({totalVertices}, максимум {limits.MaxTotalVertices})");
        }

        if (errors.Count >= MaxReportedErrors)
        {
            errors.Add("…показаны первые ошибки, остальные проверь после исправления");
        }

        return errors;
    }

    public static void ValidateLayer(LayerSpec? layer, string path, List<string> errors)
    {
        if (layer is null)
        {
            errors.Add($"{path}: пустой слой");
            return;
        }

        if (!SymbolNames.IsValid(layer.Name, out var nameError))
        {
            errors.Add($"{path}.name: {nameError}");
        }

        ValidateColor(layer.Color, $"{path}.color", errors);

        if (layer.LineWeight is { } lw && !ValidLineWeights.Contains(lw))
        {
            errors.Add($"{path}.lineWeight: {lw} не входит в список допустимых весов линий");
        }
    }

    private static void ValidateEntity(EntitySpec? e, string path, Limits limits, List<string> errors)
    {
        if (e is null)
        {
            errors.Add($"{path}: пустой объект");
            return;
        }

        var type = e.Type?.Trim().ToLowerInvariant() ?? "";
        if (!EntityTypes.All.Contains(type))
        {
            errors.Add($"{path}.type: неизвестный тип «{e.Type}». Допустимо: {string.Join(", ", EntityTypes.All)}");
            return;
        }

        if (e.Layer is not null && !SymbolNames.IsValid(e.Layer, out var layerError))
        {
            errors.Add($"{path}.layer: {layerError}");
        }

        ValidateColor(e.Color, $"{path}.color", errors);

        switch (type)
        {
            case EntityTypes.Line:
                if (e.Points is not { Count: 2 })
                {
                    errors.Add($"{path}.points: у отрезка должно быть ровно 2 точки");
                }
                else
                {
                    ValidatePoints(e.Points, $"{path}.points", limits, errors);
                    if (e.Points[0].X == e.Points[1].X && e.Points[0].Y == e.Points[1].Y)
                    {
                        errors.Add($"{path}.points: начало и конец отрезка совпадают");
                    }
                }

                break;

            case EntityTypes.Polyline:
                if (e.Points is null || e.Points.Count < 2)
                {
                    errors.Add($"{path}.points: у полилинии должно быть минимум 2 точки");
                }
                else if (e.Points.Count > limits.MaxPolylineVertices)
                {
                    errors.Add($"{path}.points: слишком много вершин ({e.Points.Count}, максимум {limits.MaxPolylineVertices})");
                }
                else
                {
                    ValidatePoints(e.Points, $"{path}.points", limits, errors);
                }

                break;

            case EntityTypes.Circle:
                ValidatePoint(e.Center, $"{path}.center", limits, errors);
                ValidatePositive(e.Radius, $"{path}.radius", limits, errors);
                break;

            case EntityTypes.Arc:
                ValidatePoint(e.Center, $"{path}.center", limits, errors);
                ValidatePositive(e.Radius, $"{path}.radius", limits, errors);
                ValidateAngle(e.StartAngle, $"{path}.startAngle", required: true, errors);
                ValidateAngle(e.EndAngle, $"{path}.endAngle", required: true, errors);
                break;

            case EntityTypes.Text:
                if (string.IsNullOrWhiteSpace(e.Text))
                {
                    errors.Add($"{path}.text: текст не может быть пустым");
                }
                else if (e.Text.Length > limits.MaxTextLength)
                {
                    errors.Add($"{path}.text: текст длиннее {limits.MaxTextLength} символов");
                }
                else if (e.Text.Any(char.IsControl))
                {
                    errors.Add($"{path}.text: однострочный текст не может содержать переносы строк и управляющие символы");
                }

                ValidatePoint(e.Position, $"{path}.position", limits, errors);
                ValidatePositive(e.Height, $"{path}.height", limits, errors);
                ValidateAngle(e.Rotation, $"{path}.rotation", required: false, errors);
                break;
        }
    }

    private static void ValidateColor(int? color, string path, List<string> errors)
    {
        if (color is { } c && (c < 1 || c > 255))
        {
            errors.Add($"{path}: цвет должен быть от 1 до 255");
        }
    }

    private static void ValidatePoints(List<Point2> points, string path, Limits limits, List<string> errors)
    {
        for (var i = 0; i < points.Count && errors.Count < MaxReportedErrors; i++)
        {
            ValidatePoint(points[i], $"{path}[{i}]", limits, errors);
        }
    }

    private static void ValidatePoint(Point2? p, string path, Limits limits, List<string> errors)
    {
        if (p is null)
        {
            errors.Add($"{path}: точка не задана");
            return;
        }

        if (!IsSane(p.X, limits) || !IsSane(p.Y, limits))
        {
            errors.Add($"{path}: координаты должны быть числами в пределах ±{limits.MaxCoordinate:G}");
        }
    }

    private static void ValidatePositive(double? value, string path, Limits limits, List<string> errors)
    {
        if (value is not { } v || !IsSane(v, limits) || v <= 0)
        {
            errors.Add($"{path}: должно быть числом больше 0 (не больше {limits.MaxCoordinate:G})");
        }
    }

    private static void ValidateAngle(double? value, string path, bool required, List<string> errors)
    {
        if (value is null)
        {
            if (required)
            {
                errors.Add($"{path}: угол не задан");
            }

            return;
        }

        if (!double.IsFinite(value.Value) || Math.Abs(value.Value) > 3600)
        {
            errors.Add($"{path}: угол должен быть числом в градусах");
        }
    }

    private static bool IsSane(double v, Limits limits) => double.IsFinite(v) && Math.Abs(v) <= limits.MaxCoordinate;
}
