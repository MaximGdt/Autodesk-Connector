namespace AutocadMcp.Core.Model;

/// <summary>
/// Size limits that keep AutoCAD responsive and stop runaway requests.
/// Settings may LOWER these, never raise them above the hard caps (see ConnectorSettings).
/// </summary>
public sealed record Limits
{
    public const int HardMaxEntities = 20_000;
    public const int HardMaxTotalVertices = 200_000;

    public int MaxEntities { get; init; } = 5_000;
    public int MaxLayersPerRequest { get; init; } = 200;
    public int MaxPolylineVertices { get; init; } = 10_000;
    public int MaxTotalVertices { get; init; } = 100_000;
    public int MaxTextLength { get; init; } = 1_000;
    public double MaxCoordinate { get; init; } = 1e7;

    public static readonly Limits Default = new();
}
