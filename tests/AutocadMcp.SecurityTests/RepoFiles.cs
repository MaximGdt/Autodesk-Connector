using System.Text;

namespace AutocadMcp.SecurityTests;

/// <summary>Finds repository files and strips comments/strings so rules check real code only.</summary>
internal static class RepoFiles
{
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AutocadMcp.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("AutocadMcp.sln not found above the test binaries.");
    }

    public static string Rel(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    public static IEnumerable<string> Sources(string folder) =>
        Directory.EnumerateFiles(Path.Combine(Root, folder), "*.cs", SearchOption.AllDirectories)
            .Where(p => !Rel(p).Contains("/bin/") && !Rel(p).Contains("/obj/"))
            .OrderBy(p => p, StringComparer.Ordinal);

    public static IEnumerable<string> Files(string pattern) =>
        Directory.EnumerateFiles(Root, pattern, SearchOption.AllDirectories)
            .Where(p => !Rel(p).Contains("/bin/") && !Rel(p).Contains("/obj/") && !Rel(p).StartsWith(".git/"));

    /// <summary>
    /// Removes comments and string/char literals (replaced by spaces, newlines kept)
    /// so a forbidden word inside a message or a comment does not count, but code does.
    /// </summary>
    public static string CodeOnly(string text)
    {
        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (c == '/' && next == '/')
            {
                while (i < text.Length && text[i] != '\n') { i++; }
                continue;
            }

            if (c == '/' && next == '*')
            {
                i += 2;
                while (i < text.Length && !(text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/'))
                {
                    sb.Append(text[i] == '\n' ? '\n' : ' ');
                    i++;
                }

                i += 2;
                continue;
            }

            if (c == '"' && next == '"' && i + 2 < text.Length && text[i + 2] == '"')
            {
                // raw string literal """ ... """
                i += 3;
                while (i < text.Length && !(text[i] == '"' && i + 2 < text.Length && text[i + 1] == '"' && text[i + 2] == '"'))
                {
                    sb.Append(text[i] == '\n' ? '\n' : ' ');
                    i++;
                }

                i += 3;
                sb.Append("\"\"");
                continue;
            }

            var verbatim = (c == '@' && next == '"') || (c == '$' && next == '@' ) || (c == '@' && next == '$');
            if (verbatim)
            {
                while (i < text.Length && text[i] != '"') { i++; }
                i++;
                while (i < text.Length)
                {
                    if (text[i] == '"' && i + 1 < text.Length && text[i + 1] == '"') { i += 2; continue; }
                    if (text[i] == '"') { i++; break; }
                    sb.Append(text[i] == '\n' ? '\n' : ' ');
                    i++;
                }

                sb.Append("\"\"");
                continue;
            }

            if (c == '"' || (c == '$' && next == '"'))
            {
                if (c == '$') { i++; }
                i++;
                while (i < text.Length && text[i] != '"' && text[i] != '\n')
                {
                    if (text[i] == '\\') { i++; }
                    i++;
                }

                i++;
                sb.Append("\"\"");
                continue;
            }

            if (c == '\'' && i + 2 < text.Length && (text[i + 2] == '\'' || (text[i + 1] == '\\' && i + 3 < text.Length && text[i + 3] == '\'')))
            {
                i += text[i + 1] == '\\' ? 4 : 3;
                sb.Append("' '");
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }
}
