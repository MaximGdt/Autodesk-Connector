using System.Text.Json;
using AutocadMcp.Core.Model;

namespace AutocadMcp.Core.Settings;

/// <summary>
/// Optional per-user settings in %LOCALAPPDATA%\AutocadMcp\settings.json.
/// Every value is clamped to safe bounds: a settings file can make the connector
/// stricter, never looser (docs/SECURITY.md, S6).
/// </summary>
public sealed record ConnectorSettings
{
    public string OutputFolder { get; init; } = DefaultOutputFolder();
    public int MaxEntities { get; init; } = Limits.Default.MaxEntities;
    public int RequestTimeoutSeconds { get; init; } = 60;
    public bool EchoToCommandLine { get; init; } = true;

    public static string AppDataFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutocadMcp");

    public static string DefaultOutputFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AutoCAD-MCP");

    public Limits ToLimits() => Limits.Default with { MaxEntities = MaxEntities };

    /// <summary>Loads settings; any problem falls back to safe defaults and is reported in <paramref name="warning"/>.</summary>
    public static ConnectorSettings Load(string? path, out string? warning)
    {
        warning = null;
        path ??= Path.Combine(AppDataFolder(), "settings.json");
        var loaded = new ConnectorSettings();

        if (File.Exists(path))
        {
            try
            {
                var text = File.ReadAllText(path);
                if (text.Length > 64 * 1024)
                {
                    throw new InvalidDataException("файл настроек слишком большой");
                }

                loaded = JsonSerializer.Deserialize<ConnectorSettings>(text, JsonWire.Options) ?? new ConnectorSettings();
            }
            catch (Exception ex)
            {
                warning = $"Файл настроек {path} не прочитан ({ex.Message}). Использую значения по умолчанию.";
                loaded = new ConnectorSettings();
            }
        }

        return loaded.Clamp();
    }

    public ConnectorSettings Clamp()
    {
        var folder = string.IsNullOrWhiteSpace(OutputFolder) || !Path.IsPathFullyQualified(OutputFolder)
            ? DefaultOutputFolder()
            : OutputFolder;

        return this with
        {
            OutputFolder = folder,
            MaxEntities = Math.Clamp(MaxEntities, 1, Limits.HardMaxEntities),
            RequestTimeoutSeconds = Math.Clamp(RequestTimeoutSeconds, 5, 300),
        };
    }
}
