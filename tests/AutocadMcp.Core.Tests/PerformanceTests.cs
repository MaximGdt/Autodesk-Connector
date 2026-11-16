using System.Diagnostics;
using AutocadMcp.Core.Model;
using AutocadMcp.Core.Protocol;

namespace AutocadMcp.Core.Tests;

/// <summary>
/// Speed budgets for the parts that run outside AutoCAD. Budgets are generous
/// (an office laptop must pass), but catch accidental slowness such as O(n²) validation.
/// AutoCAD-side budgets are checked by the --smoke self-test (docs/ACCEPTANCE.md).
/// </summary>
public class PerformanceTests
{
    [Fact]
    public void Validating5000Entities_TakesUnder250ms()
    {
        var spec = new DrawingSpec();
        for (var i = 0; i < 5000; i++)
        {
            spec.Entities.Add(new EntitySpec
            {
                Type = "polyline",
                Layer = "L" + (i % 20),
                Points = [new(i, 0), new(i + 1, 0), new(i + 1, 1), new(i, 1)],
                Closed = true,
            });
        }

        SpecValidator.Validate(spec, Limits.Default); // warm-up
        var sw = Stopwatch.StartNew();
        var errors = SpecValidator.Validate(spec, Limits.Default);
        sw.Stop();

        Assert.Empty(errors);
        Assert.True(sw.ElapsedMilliseconds < 250, $"validation took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Framing1MB_TakesUnder200ms()
    {
        var payload = "{\"x\":\"" + new string('a', 1024 * 1024) + "\"}";
        using var ms = new MemoryStream();
        var sw = Stopwatch.StartNew();
        await FrameCodec.WriteAsync(ms, payload, CancellationToken.None);
        ms.Position = 0;
        var back = await FrameCodec.ReadAsync(ms, CancellationToken.None);
        sw.Stop();

        Assert.Equal(payload.Length, back!.Length);
        Assert.True(sw.ElapsedMilliseconds < 200, $"framing took {sw.ElapsedMilliseconds} ms");
    }
}
