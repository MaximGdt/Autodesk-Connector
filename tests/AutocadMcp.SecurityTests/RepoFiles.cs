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
    /// Code inside {holes} of interpolated strings is KEPT: a forbidden call must not hide there.
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

            if (c == '$' || (c == '@' && next == '$'))
            {
                // Interpolated string: $"…", $@"…", @$"…", $"""…""", $$"""…""" …
                var j = i;
                var isVerbatim = false;
                if (text[j] == '@') { isVerbatim = true; j++; }
                var dollars = 0;
                while (j < text.Length && text[j] == '$') { dollars++; j++; }
                if (j < text.Length && text[j] == '@') { isVerbatim = true; j++; }
                if (dollars > 0 && j < text.Length && text[j] == '"')
                {
                    i = SkipInterpolated(text, j, dollars, isVerbatim, sb);
                    continue;
                }
            }

            if (c == '"' && next == '"' && i + 2 < text.Length && text[i + 2] == '"')
            {
                // raw string literal: N quotes (N >= 3) … the same N quotes
                var quotes = Run(text, i, '"');
                i += quotes;
                while (i < text.Length && Run(text, i, '"') < quotes)
                {
                    sb.Append(text[i] == '\n' ? '\n' : ' ');
                    i++;
                }

                i += quotes;
                sb.Append("\"\"");
                continue;
            }

            var verbatim = c == '@' && next == '"';
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

            if (c == '"')
            {
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

    private static int Run(string text, int i, char ch)
    {
        var n = 0;
        while (i + n < text.Length && text[i + n] == ch) { n++; }
        return n;
    }

    /// <summary>Blanks the text parts of an interpolated string and copies the code of its holes.</summary>
    private static int SkipInterpolated(string text, int quote, int dollars, bool verbatim, StringBuilder sb)
    {
        var quotes = Run(text, quote, '"');
        var raw = quotes >= 3;
        if (!raw && quotes == 2)
        {
            sb.Append("\"\""); // $"" — empty string
            return quote + 2;
        }

        if (!raw) { quotes = 1; }
        var i = quote + quotes;
        var holeOpen = raw ? dollars : 1;

        while (i < text.Length)
        {
            var ch = text[i];
            if (raw)
            {
                if (ch == '"' && Run(text, i, '"') >= quotes) { i += quotes; break; }
            }
            else if (verbatim)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { sb.Append("  "); i += 2; continue; }
                if (ch == '"') { i++; break; }
            }
            else
            {
                if (ch == '\\' && i + 1 < text.Length && text[i + 1] != '\n') { sb.Append("  "); i += 2; continue; }
                if (ch == '"') { i++; break; }
                if (ch == '\n') { break; }
            }

            if (ch == '{')
            {
                var run = Run(text, i, '{');
                if (!raw && run >= 2)
                {
                    sb.Append("  "); // {{ is a literal brace
                    i += 2;
                    continue;
                }

                sb.Append(' ', run);
                i += run;
                if (run >= holeOpen)
                {
                    i = CopyHole(text, i, sb);
                }

                continue;
            }

            sb.Append(ch == '\n' ? '\n' : ' ');
            i++;
        }

        sb.Append("\"\"");
        return i;
    }

    /// <summary>Copies the code of one interpolation hole up to its closing brace.</summary>
    private static int CopyHole(string text, int i, StringBuilder sb)
    {
        var depth = 0;
        while (i < text.Length)
        {
            var ch = text[i];
            if (ch == '"')
            {
                // A string inside the hole is copied as is: its braces must not count.
                var end = i + 1;
                while (end < text.Length && text[end] != '"' && text[end] != '\n')
                {
                    end += text[end] == '\\' ? 2 : 1;
                }

                end = Math.Min(end + 1, text.Length);
                sb.Append(text, i, end - i);
                i = end;
                continue;
            }

            if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}')
            {
                if (depth == 0)
                {
                    sb.Append(' ');
                    return i + 1;
                }

                depth--;
            }

            sb.Append(ch);
            i++;
        }

        return i;
    }
}
