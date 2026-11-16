using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AutocadMcp.Core;
using ModelContextProtocol.Server;

namespace AutocadMcp.SecurityTests;

/// <summary>Checks that the project-level safety nets are in place and were not quietly removed.</summary>
public class GuardrailTests
{
    private static readonly HashSet<string> AllowedPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "AutoCAD.NET", "ModelContextProtocol", "Microsoft.Extensions.Hosting",
        "Microsoft.NET.Test.Sdk", "xunit", "xunit.runner.visualstudio",
    };

    [Fact]
    public void McpServer_ExposesExactlyTheAllowlistedTools()
    {
        var exposed = typeof(AutocadMcp.Server.PipeClient).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t.GetMethods())
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>())
            .Where(a => a is not null)
            .Select(a => a!.Name ?? "<без имени>")
            .ToHashSet();

        Assert.True(exposed.SetEquals(ToolNames.All),
            "[S1] Инструменты MCP-сервера должны совпадать со списком Core/ToolNames.All.\n" +
            $"Лишние: {string.Join(", ", exposed.Except(ToolNames.All))}\nНедостающие: {string.Join(", ", ToolNames.All.Except(exposed))}");
    }

    [Fact]
    public void ClaudeDesktopManifest_ListsExactlyTheAllowlistedTools()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoFiles.Root, "packaging/mcpb/manifest.json")));
        var listed = json.RootElement.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()!).ToHashSet();
        Assert.True(listed.SetEquals(ToolNames.All), "packaging/mcpb/manifest.json: список tools должен совпадать с Core/ToolNames.All");

        var cfg = json.RootElement.GetProperty("server").GetProperty("mcp_config");
        Assert.Equal(0, cfg.GetProperty("args").GetArrayLength());
        Assert.False(cfg.TryGetProperty("env", out _), "Расширению не нужны переменные окружения");
    }

    [Fact]
    public void EveryTool_IsDocumented()
    {
        var doc = File.ReadAllText(Path.Combine(RepoFiles.Root, "docs/TOOLS.md"));
        var missing = ToolNames.All.Where(t => !doc.Contains($"`{t}`", StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, "Каждый инструмент должен быть описан в docs/TOOLS.md: " + string.Join(", ", missing));
    }

    [Fact]
    public void OnlyAllowlistedPackages_AreUsed()
    {
        var central = XDocument.Load(Path.Combine(RepoFiles.Root, "Directory.Packages.props"))
            .Descendants("PackageVersion").Select(e => (string?)e.Attribute("Include") ?? "").ToList();
        Assert.True(central.All(AllowedPackages.Contains),
            "[S7] В Directory.Packages.props есть неразрешённый пакет: " + string.Join(", ", central.Where(p => !AllowedPackages.Contains(p))));

        foreach (var proj in RepoFiles.Files("*.csproj"))
        {
            var xml = XDocument.Load(proj);
            foreach (var pr in xml.Descendants("PackageReference"))
            {
                var name = (string?)pr.Attribute("Include") ?? "";
                Assert.True(AllowedPackages.Contains(name), $"[S7] {RepoFiles.Rel(proj)} ссылается на неразрешённый пакет {name}");
                Assert.True(pr.Attribute("Version") is null && pr.Attribute("VersionOverride") is null,
                    $"[S7] {RepoFiles.Rel(proj)}: версия пакета {name} задаётся только в Directory.Packages.props");
            }

            Assert.DoesNotContain(xml.Descendants("Reference"), r => r.Element("HintPath") is not null || r.Attribute("HintPath") is not null);
        }
    }

    [Fact]
    public void NoExtraPackageSources()
    {
        foreach (var cfg in RepoFiles.Files("*.config").Where(p => Path.GetFileName(p).Equals("nuget.config", StringComparison.OrdinalIgnoreCase)))
        {
            var sources = XDocument.Load(cfg).Descendants("add").Select(a => (string?)a.Attribute("value") ?? "");
            Assert.All(sources, s => Assert.StartsWith("https://api.nuget.org/", s));
        }
    }

    [Fact]
    public void AutocadAssemblies_AreNeverShipped()
    {
        var xml = XDocument.Load(Path.Combine(RepoFiles.Root, "src/AutocadMcp.Plugin/AutocadMcp.Plugin.csproj"));
        var acad = xml.Descendants("PackageReference").Single(e => (string?)e.Attribute("Include") == "AutoCAD.NET");
        Assert.Equal("runtime", (string?)acad.Attribute("ExcludeAssets"));
    }

    [Fact]
    public void ClaudeCodePermissions_KeepTheirDenyRules()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoFiles.Root, ".claude/settings.json")));
        var deny = json.RootElement.GetProperty("permissions").GetProperty("deny").EnumerateArray().Select(e => e.GetString()).ToHashSet();

        string[] required =
        [
            "Bash(git push --force:*)",
            "Bash(rm -rf:*)",
            "Bash(curl:*)",
            "Edit(./docs/SECURITY.md)",
            "Edit(./tests/AutocadMcp.SecurityTests/**)",
            "Edit(./.claude/settings.json)",
            "Edit(./Directory.Packages.props)",
            "Edit(./Directory.Build.props)",
            "Edit(./global.json)",
            "Edit(./.github/**)",
            "Bash(git branch -D:*)",
            "Bash(git tag -d:*)",
        ];

        var missing = required.Where(r => !deny.Contains(r)).ToList();
        Assert.True(missing.Count == 0, "В .claude/settings.json пропали запреты: " + string.Join(", ", missing));
    }

    [Fact]
    public void ProtectedFiles_RequireMaximsReview()
    {
        var owners = File.ReadAllText(Path.Combine(RepoFiles.Root, ".github/CODEOWNERS"));
        string[] protectedPaths =
        [
            "/docs/SECURITY.md", "/tests/AutocadMcp.SecurityTests/", "/.claude/", "/.github/",
            "/Directory.Packages.props", "/Directory.Build.props", "/global.json",
            "/src/AutocadMcp.Core/Safety/", "/src/AutocadMcp.Core/ToolNames.cs", "/src/AutocadMcp.Core/Protocol/",
            // Files with special rights or that hold a safety rule:
            "/src/AutocadMcp.Core/Audit/", "/src/AutocadMcp.Core/Settings/", "/src/AutocadMcp.Core/Model/Limits.cs",
            "/src/AutocadMcp.Plugin/Handlers/SaveDwgHandler.cs", "/src/AutocadMcp.Plugin/PipeHost.cs",
            "/src/AutocadMcp.Plugin/MainThreadDispatcher.cs", "/packaging/",
        ];
        var missing = protectedPaths.Where(p => !owners.Contains(p)).ToList();
        Assert.True(missing.Count == 0, "CODEOWNERS не защищает: " + string.Join(", ", missing));
    }

    [Fact]
    public void NoTest_IsSkippedOrDisabled()
    {
        var offenders = RepoFiles.Sources("tests")
            .Where(f => Regex.IsMatch(RepoFiles.CodeOnly(File.ReadAllText(f)), @"\bSkip\s*=|\[\s*Trait\s*\(\s*""\s*Category""\s*,\s*""\s*Skip"))
            .Select(RepoFiles.Rel)
            .ToList();
        Assert.True(offenders.Count == 0, "Тесты нельзя отключать (Skip): " + string.Join(", ", offenders));
    }
}
