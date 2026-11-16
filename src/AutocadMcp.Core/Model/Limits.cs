namespace AutocadMcp.Core.Model;

/// <summary>
/// Size limits that keep AutoCAD responsive and stop runaway requests.
/// The settings file may only LOWER MaxEntities; nothing here can be raised without
/// changing this file, which needs Maxim's review (CODEOWNERS, docs/SECURITY.md S6).
/// </summary>
public sealed record Limits
{
    public int MaxEntities { get; init; } = 5_000;
    public int MaxLayersPerRequest { get; init; } = 200;
    public int MaxPolylineVertices { get; init; } = 10_000;
    public int MaxTotalVertices { get; init; } = 100_000;
    public int MaxTextLength { get; init; } = 1_000;
    public double MaxCoordinate { get; init; } = 1e7;

    public static readonly Limits Default = new();
}
