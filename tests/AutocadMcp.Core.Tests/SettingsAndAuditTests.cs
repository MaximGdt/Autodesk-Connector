using AutocadMcp.Core.Audit;
using AutocadMcp.Core.Model;
using AutocadMcp.Core.Settings;

namespace AutocadMcp.Core.Tests;

public class SettingsAndAuditTests
{
    [Fact]
    public void Settings_CannotRaiseLimitsAboveHardCaps()
    {
        var dir = Directory.CreateTempSubdirectory();
        var file = Path.Combine(dir.FullName, "settings.json");
        File.WriteAllText(file, "{\"maxEntities\": 99999999, \"requestTimeoutSeconds\": 100000, \"outputFolder\": \"relative\\\\path\"}");

        var s = ConnectorSettings.Load(file, out var warning);

        Assert.Null(warning);
        Assert.Equal(Limits.HardMaxEntities, s.MaxEntities);
        Assert.Equal(300, s.RequestTimeoutSeconds);
        Assert.Equal(ConnectorSettings.DefaultOutputFolder(), s.OutputFolder); // relative path ignored
    }

    [Fact]
    public void BrokenSettings_FallBackToDefaultsWithWarning()
    {
        var dir = Directory.CreateTempSubdirectory();
        var file = Path.Combine(dir.FullName, "settings.json");
        File.WriteAllText(file, "{ not json");

        var s = ConnectorSettings.Load(file, out var warning);

        Assert.NotNull(warning);
        Assert.Equal(Limits.Default.MaxEntities, s.MaxEntities);
    }

    [Fact]
    public void MissingSettings_AreDefaults()
    {
        var s = ConnectorSettings.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), out var warning);
        Assert.Null(warning);
        Assert.Equal(new ConnectorSettings().Clamp(), s);
    }

    [Fact]
    public void AuditLog_WritesOneTruncatedLinePerEntry()
    {
        var dir = Directory.CreateTempSubdirectory();
        var log = new AuditLog(dir.FullName);
        var ts = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

        log.Write(new AuditEntry(ts, "r1", "build_drawing", true, null, 12, 9999, new string('x', 5000)));
        log.Write(new AuditEntry(ts, "r2", "save_dwg", false, "path_denied", 1, 20, "{\"fileName\":\"..\\\\evil\"}"));

        var lines = File.ReadAllLines(Path.Combine(dir.FullName, "audit-20261116.jsonl"));
        Assert.Equal(2, lines.Length);
        Assert.True(lines[0].Length < 1000);
        Assert.Contains("path_denied", lines[1]);
    }

    [Fact]
    public void AuditLog_NeverThrows_WhenFolderIsUnwritable()
    {
        var file = Path.GetTempFileName(); // a FILE where a folder is expected
        var log = new AuditLog(file);
        log.Write(new AuditEntry(DateTimeOffset.Now, "r", "t", true, null, 0, 0, null));
    }
}
