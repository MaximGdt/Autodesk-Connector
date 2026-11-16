using AutocadMcp.Core.Safety;

namespace AutocadMcp.Core.Tests;

public class PathGuardTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "mcp-out");

    [Theory]
    [InlineData("plan", "plan.dwg")]
    [InlineData("plan.dwg", "plan.dwg")]
    [InlineData("План этажа 1.DWG", "План этажа 1.DWG")]
    public void PlainNames_ResolveInsideRoot(string input, string expectedName)
    {
        Assert.True(PathGuard.TryResolveDwgPath(Root, input, out var full, out var error), error);
        Assert.Equal(Path.Combine(Path.GetFullPath(Root), expectedName), full);
    }

    [Theory]
    [InlineData("..\\evil.dwg")]
    [InlineData("../evil.dwg")]
    [InlineData("sub/evil.dwg")]
    [InlineData("sub\\evil.dwg")]
    [InlineData("C:\\Windows\\evil.dwg")]
    [InlineData("C:evil.dwg")]
    [InlineData("\\\\server\\share\\evil.dwg")]
    [InlineData("/etc/evil.dwg")]
    [InlineData("..")]
    [InlineData(".hidden")]
    [InlineData("CON")]
    [InlineData("nul.dwg")]
    [InlineData("com1.backup.dwg")]
    [InlineData("COM0.dwg")]
    [InlineData("lpt0")]
    [InlineData("CONIN$")]
    [InlineData("conout$.dwg")]
    [InlineData("COM¹.dwg")]
    [InlineData("con .dwg")]
    [InlineData("a?b.dwg")]
    [InlineData("a*b.dwg")]
    [InlineData("name.")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void DangerousNames_AreRefused(string? input)
    {
        Assert.False(PathGuard.TryResolveDwgPath(Root, input, out var full, out var error));
        Assert.Equal("", full);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void VeryLongName_IsRefused()
    {
        Assert.False(PathGuard.TryResolveDwgPath(Root, new string('a', 200), out _, out _));
    }

    [Fact]
    public void ControlCharacters_AreRefused()
    {
        Assert.False(PathGuard.TryResolveDwgPath(Root, "a\tb.dwg", out _, out _));
    }
}
