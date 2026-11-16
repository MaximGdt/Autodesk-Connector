namespace AutocadMcp.Core.Safety;

/// <summary>
/// The ONLY way a file path is produced for writing a drawing (docs/SECURITY.md, S5).
/// Claude may pass a bare file name; the folder is always the configured output folder.
/// Directories, "..", drive letters, UNC paths and reserved device names are refused.
/// </summary>
public static class PathGuard
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    // Characters Windows refuses in file names, checked explicitly so behaviour is the same on every OS.
    private static readonly char[] InvalidFileNameChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    public const int MaxFileNameLength = 120;

    /// <summary>
    /// Turns a requested file name into a full path inside <paramref name="outputFolder"/>.
    /// Returns false with a Russian explanation when the name is not acceptable.
    /// </summary>
    public static bool TryResolveDwgPath(string outputFolder, string? requestedName, out string fullPath, out string error)
    {
        fullPath = "";
        if (string.IsNullOrWhiteSpace(requestedName))
        {
            error = "не указано имя файла";
            return false;
        }

        var name = requestedName.Trim();
        if (name.IndexOfAny(InvalidFileNameChars) >= 0 || name.Any(char.IsControl))
        {
            error = "нужно только имя файла, без папок и без символов < > : \" / \\ | ? *";
            return false;
        }

        if (name.Contains("..", StringComparison.Ordinal) || name.StartsWith('.') || name.EndsWith(' ') || name.EndsWith('.'))
        {
            error = "недопустимое имя файла";
            return false;
        }

        if (!name.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
        {
            name += ".dwg";
        }

        if (name.Length > MaxFileNameLength)
        {
            error = $"имя файла длиннее {MaxFileNameLength} символов";
            return false;
        }

        var stem = name[..^4];
        // Windows ignores trailing spaces before the extension: "CON .dwg" is still the CON device.
        var deviceStem = stem.Split('.')[0].TrimEnd(' ');
        if (ReservedNames.Contains(deviceStem))
        {
            error = "это зарезервированное имя Windows";
            return false;
        }

        var root = Path.GetFullPath(outputFolder);
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(root, name));

        if (!candidate.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(candidate)?.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            error = "сохранять можно только в разрешённую папку";
            return false;
        }

        fullPath = candidate;
        error = "";
        return true;
    }
}
