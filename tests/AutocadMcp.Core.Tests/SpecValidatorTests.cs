using AutocadMcp.Core.Model;

namespace AutocadMcp.Core.Tests;

public class SpecValidatorTests
{
    private static DrawingSpec One(EntitySpec e) => new() { Entities = [e] };

    private static IReadOnlyList<string> Check(DrawingSpec spec) => SpecValidator.Validate(spec, Limits.Default);

    [Fact]
    public void ValidMixedSpec_Passes()
    {
        var spec = new DrawingSpec
        {
            Layers = [new LayerSpec { Name = "Стены", Color = 1, LineWeight = 50 }],
            Entities =
            [
                new EntitySpec { Type = "line", Layer = "Стены", Points = [new(0, 0), new(10, 0)] },
                new EntitySpec { Type = "polyline", Closed = true, Points = [new(0, 0), new(4, 0), new(4, 5)] },
                new EntitySpec { Type = "circle", Center = new(1, 1), Radius = 2 },
                new EntitySpec { Type = "arc", Center = new(1, 1), Radius = 2, StartAngle = 0, EndAngle = 90 },
                new EntitySpec { Type = "TEXT", Text = "Комната 101", Position = new(0, 0), Height = 2.5 },
            ],
        };

        Assert.Empty(Check(spec));
    }

    [Theory]
    [InlineData("explode")]
    [InlineData("lisp")]
    [InlineData("")]
    public void UnknownType_IsRejected(string type)
    {
        var errors = Check(One(new EntitySpec { Type = type }));
        Assert.Contains(errors, e => e.Contains("неизвестный тип"));
    }

    [Fact]
    public void EmptySpec_IsRejected()
    {
        Assert.NotEmpty(Check(new DrawingSpec()));
        Assert.NotEmpty(SpecValidator.Validate(null, Limits.Default));
    }

    [Fact]
    public void LineNeedsExactlyTwoDistinctPoints()
    {
        Assert.NotEmpty(Check(One(new EntitySpec { Type = "line", Points = [new(0, 0)] })));
        Assert.NotEmpty(Check(One(new EntitySpec { Type = "line", Points = [new(1, 1), new(1, 1)] })));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e9)]
    public void InsaneCoordinates_AreRejected(double x)
    {
        var errors = Check(One(new EntitySpec { Type = "line", Points = [new(x, 0), new(1, 1)] }));
        Assert.NotEmpty(errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NonPositiveRadius_IsRejected(double r)
    {
        Assert.NotEmpty(Check(One(new EntitySpec { Type = "circle", Center = new(0, 0), Radius = r })));
    }

    [Theory]
    [InlineData("bad/name")]
    [InlineData("a*b")]
    [InlineData(" lead")]
    [InlineData("")]
    public void BadLayerNames_AreRejected(string name)
    {
        var spec = new DrawingSpec
        {
            Layers = [new LayerSpec { Name = name }],
            Entities = [new EntitySpec { Type = "circle", Center = new(0, 0), Radius = 1 }],
        };
        Assert.NotEmpty(Check(spec));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    public void ColorOutOfRange_IsRejected(int color)
    {
        Assert.NotEmpty(Check(One(new EntitySpec { Type = "circle", Center = new(0, 0), Radius = 1, Color = color })));
    }

    [Fact]
    public void InvalidLineWeight_IsRejected()
    {
        var spec = new DrawingSpec
        {
            Layers = [new LayerSpec { Name = "A", LineWeight = 17 }],
            Entities = [new EntitySpec { Type = "circle", Center = new(0, 0), Radius = 1 }],
        };
        Assert.NotEmpty(Check(spec));
    }

    [Fact]
    public void MultilineText_IsRejected()
    {
        var e = new EntitySpec { Type = "text", Text = "a\nb", Position = new(0, 0), Height = 1 };
        Assert.Contains(Check(One(e)), x => x.Contains("переносы"));
    }

    [Fact]
    public void TooManyEntities_IsRejectedWithoutWalkingAll()
    {
        var spec = new DrawingSpec();
        for (var i = 0; i < Limits.Default.MaxEntities + 1; i++)
        {
            spec.Entities.Add(new EntitySpec { Type = "circle", Center = new(0, 0), Radius = 1 });
        }

        var errors = Check(spec);
        Assert.Single(errors);
        Assert.Contains("слишком много объектов", errors[0]);
    }

    [Fact]
    public void ErrorListIsCapped()
    {
        var spec = new DrawingSpec();
        for (var i = 0; i < 200; i++)
        {
            spec.Entities.Add(new EntitySpec { Type = "nope" });
        }

        Assert.True(Check(spec).Count <= 26);
    }
}
