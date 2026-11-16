namespace AutocadMcp.Core.Safety;

/// <summary>AutoCAD symbol-table name rules (layers, later blocks/styles).</summary>
public static class SymbolNames
{
    private static readonly char[] Forbidden = ['<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', ',', '=', '`'];

    public static bool IsValid(string? name, out string error)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "имя не может быть пустым";
            return false;
        }

        if (name.Length > 255)
        {
            error = "имя длиннее 255 символов";
            return false;
        }

        if (name != name.Trim())
        {
            error = "имя не должно начинаться или заканчиваться пробелом";
            return false;
        }

        if (name.IndexOfAny(Forbidden) >= 0 || name.Any(char.IsControl))
        {
            error = "имя содержит недопустимый символ (< > / \\ \" : ; ? * | , = ` или управляющий)";
            return false;
        }

        error = "";
        return true;
    }
}
