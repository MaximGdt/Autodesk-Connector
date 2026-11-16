using System.Text;

namespace AutocadMcp.Core.Audit;

/// <summary>
/// Append-only journal of every request: when, which tool, how big, result, how long.
/// One JSON line per request in %LOCALAPPDATA%\AutocadMcp\logs\audit-YYYYMMDD.jsonl.
/// This is the ONLY class allowed to write files directly (checked by SecurityTests).
/// Logging must never break drawing, so all I/O errors are swallowed.
/// </summary>
public sealed class AuditLog
{
    public const int MaxPreviewChars = 500;

    private readonly string _folder;
    private readonly object _gate = new();

    public AuditLog(string folder)
    {
        _folder = folder;
    }

    public string Folder => _folder;

    public static AuditLog ForCurrentUser() =>
        new(Path.Combine(Settings.ConnectorSettings.AppDataFolder(), "logs"));

    public void Write(AuditEntry entry)
    {
        try
        {
            var line = JsonWire.Serialize(entry with { ArgsPreview = Truncate(entry.ArgsPreview) });
            var file = Path.Combine(_folder, $"audit-{entry.Timestamp:yyyyMMdd}.jsonl");
            lock (_gate)
            {
                Directory.CreateDirectory(_folder);
                File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (IOException)
        {
            // Logging must never stop the drawing.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static string Truncate(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        return text.Length <= MaxPreviewChars ? text : text[..MaxPreviewChars] + "…";
    }
}

public sealed record AuditEntry(
    DateTimeOffset Timestamp,
    string RequestId,
    string Tool,
    bool Ok,
    string? ErrorCode,
    long DurationMs,
    int ArgsBytes,
    string? ArgsPreview);
