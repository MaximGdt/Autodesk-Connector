using System.Text.RegularExpressions;

namespace AutocadMcp.SecurityTests;

/// <summary>
/// Each rule = something the connector must never be able to do, the rule id from docs/SECURITY.md,
/// and the only files (if any) where the API is allowed. A red test names the file and line.
/// </summary>
public class ForbiddenApiTests
{
    public sealed record Rule(string Id, string Why, string Pattern, params string[] AllowedFiles)
    {
        public override string ToString() => Id;
    }

    public static readonly Rule[] Rules =
    [
        new("S1-no-commands",
            "Запрещено выполнять команды AutoCAD или LISP по строке: так можно сделать что угодно, включая запуск программ.",
            @"\bSendStringToExecute\b|\bPostCommand\b|\bSendCommand\b|\baced(Command|Invoke|Cmd)\w*|\b(Evaluate|Invoke)Lisp\b|\b(AcApp|Application)\s*\.\s*Invoke\s*\(|\.\s*Command(Async)?\s*\(|\bResultBuffer\b"),

        new("S3-no-processes",
            "Запрещено запускать программы и процессы.",
            @"\bProcess\s*\.\s*Start\b|\bProcessStartInfo\b|\bShellExecute\w*|\btypeof\s*\(\s*(global::)?(System\s*\.\s*Diagnostics\s*\.\s*)?Process\s*\)"),

        new("S3-S10-no-hidden-aliases",
            "Запрещено прятать опасные типы за псевдонимом или using static: так обходится проверка по именам.",
            @"\busing\s+(static\s+)?(\w+\s*=\s*)?(global::)?(System\s*\.\s*(Diagnostics\s*\.\s*Process|Net|Reflection|Runtime\s*\.\s*InteropServices|Runtime\s*\.\s*Loader)|Microsoft\s*\.\s*Win32)\b"),

        new("S4-no-network",
            "Запрещена любая сеть: коннектор работает только локально.",
            @"\bHttpClient\b|\bWebClient\b|\b(Http)?WebRequest\b|\bTcp(Client|Listener)\b|\bUdpClient\b|\bSocket\b|\bClientWebSocket\b|\bHttpListener\b|\bSmtpClient\b|\bDns\s*\.|\bWithHttpTransport\b|\bMapMcp\b|\bKestrel\b|\bAspNetCore\b"),

        new("S8-no-dynamic-code",
            "Запрещено загружать или генерировать код на лету.",
            @"\bAssembly\s*\.\s*Load(From|File)?\b|\bAssemblyLoadContext\b|\bCSharpScript\b|\bMicrosoft\s*\.\s*CodeAnalysis\b|\bReflection\s*\.\s*Emit\b|\bDynamicMethod\b|\bActivator\s*\.\s*CreateInstance\b|\bType\s*\.\s*GetType\s*\(|\bInvokeMember\b|\bMethodInfo\b|\bGetMethods?\s*\(|\bGetTypeInfo\b|\bCreateDelegate\b"),

        new("S9-no-native-code",
            "Запрещены вызовы нативного кода и unsafe.",
            @"\bDllImport\b|\bLibraryImport\b|\bunsafe\b|\bMarshal\s*\.|\bNativeLibrary\b"),

        new("S5-file-writes-only-in-audit-log",
            "Писать файлы напрямую может только журнал. Чертёж сохраняется только через save_dwg.",
            @"\bFile\s*\.\s*(Write|Append|Create|Copy|Move|Replace|Open|SetAttributes)\w*|\bnew\s+(FileStream|StreamWriter|BinaryWriter)\b|\bFileInfo\b[^;]*\.\s*(Create|Open|CopyTo|MoveTo|Replace)\w*\s*\(",
            "src/AutocadMcp.Core/Audit/AuditLog.cs"),

        new("S5-no-deleting-files",
            "Коннектор никогда не удаляет файлы и папки.",
            @"\b(File|Directory)\s*\.\s*Delete\b|\b(FileInfo|DirectoryInfo|FileSystemInfo)\b[^;]*\.\s*Delete\s*\("),

        new("S5-save-only-through-save-dwg",
            "Сохранение чертежа (SaveAs/Wblock) разрешено только в SaveDwgHandler, где путь проверяет PathGuard.",
            @"\.\s*SaveAs\s*\(|\.\s*Wblock\s*\(|\.\s*SaveAsAsync\s*\(",
            "src/AutocadMcp.Plugin/Handlers/SaveDwgHandler.cs"),

        new("S10-no-registry-or-env",
            "Запрещено менять реестр и переменные окружения.",
            @"\bRegistry(Key)?\b|\bEnvironment\s*\.\s*SetEnvironmentVariable\b"),

        new("S2-no-pipe-acl-widening",
            "Запрещено расширять права на канал: доступ только у текущего пользователя.",
            @"\bPipeSecurity\b|\bNamedPipeServerStreamAcl\b|\bPipeAccessRule\b"),
    ];

    public static TheoryData<Rule> AllRules()
    {
        var data = new TheoryData<Rule>();
        foreach (var r in Rules)
        {
            data.Add(r);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllRules))]
    public void SourceCode_DoesNotUseForbiddenApi(Rule rule)
    {
        var regex = new Regex(rule.Pattern, RegexOptions.Compiled);
        var violations = new List<string>();

        foreach (var file in RepoFiles.Sources("src"))
        {
            var rel = RepoFiles.Rel(file);
            if (rule.AllowedFiles.Contains(rel))
            {
                continue;
            }

            var lines = RepoFiles.CodeOnly(File.ReadAllText(file)).Split('\n');
            for (var n = 0; n < lines.Length; n++)
            {
                var m = regex.Match(lines[n]);
                if (m.Success)
                {
                    violations.Add($"{rel}:{n + 1}: «{m.Value}»");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"[{rule.Id}] {rule.Why}\nНарушения:\n  " + string.Join("\n  ", violations) +
            "\nНе ослабляй это правило. Если это действительно нужно, остановись и спроси Максима.");
    }

    [Fact]
    public void SaveDwgHandler_UsesPathGuard()
    {
        var code = RepoFiles.CodeOnly(File.ReadAllText(Path.Combine(RepoFiles.Root, "src/AutocadMcp.Plugin/Handlers/SaveDwgHandler.cs")));
        Assert.Contains("PathGuard.TryResolveDwgPath", code);
    }

    [Fact]
    public void EveryPipe_IsCurrentUserOnly()
    {
        var offenders = new List<string>();
        foreach (var file in RepoFiles.Sources("src"))
        {
            var code = RepoFiles.CodeOnly(File.ReadAllText(file));
            var pipes = Regex.Matches(code, @"new\s+NamedPipe(Server|Client)Stream\s*\(").Count;
            var guarded = Regex.Matches(code, @"PipeOptions\s*\.\s*CurrentUserOnly").Count;
            if (pipes > guarded)
            {
                offenders.Add(RepoFiles.Rel(file));
            }
        }

        Assert.True(offenders.Count == 0, "[S2] Каждый канал должен открываться с PipeOptions.CurrentUserOnly: " + string.Join(", ", offenders));
    }

    [Fact]
    public void CodeOnly_StripsCommentsAndStringsButKeepsCode()
    {
        var sample = "// SendStringToExecute\nvar a = \"Process.Start\"; /* HttpClient */ x.SaveAs(p);";
        var code = RepoFiles.CodeOnly(sample);
        Assert.DoesNotContain("SendStringToExecute", code);
        Assert.DoesNotContain("Process.Start", code);
        Assert.DoesNotContain("HttpClient", code);
        Assert.Contains("x.SaveAs(p);", code);
    }

    [Fact]
    public void CodeOnly_KeepsCodeInsideInterpolationHoles()
    {
        // A forbidden call must not hide inside {…} of an interpolated string.
        var sample = """"
            var a = $"x {Process.Start("cmd")} y";
            var b = $@"{HttpClient.Foo()}";
            var c = $$"""{ {{Assembly.LoadFrom(p)}} }""";
            var d = $"{{WebClient}}";
            var e = "Socket";
            """";
        var code = RepoFiles.CodeOnly(sample);
        Assert.Contains("Process.Start(", code);
        Assert.Contains("HttpClient.Foo()", code);
        Assert.Contains("Assembly.LoadFrom(p)", code);
        Assert.DoesNotContain("WebClient", code); // {{ }} is literal text, not code
        Assert.DoesNotContain("Socket", code);
        Assert.Equal(sample.Split('\n').Length, code.Split('\n').Length); // line numbers stay right
    }

    [Fact]
    public void Rules_CatchAKnownBadSnippet()
    {
        // Proves the patterns are alive: each must match at least one sample.
        string[] bad =
        [
            "doc.SendStringToExecute(cmd, true, false, false);",
            "Process.Start(\"cmd.exe\");",
            "var s = $\"{Process.Start(\"cmd.exe\")}\";",
            "using P = System.Diagnostics.Process;",
            "using var http = new HttpClient();",
            "Assembly.LoadFrom(path);",
            "[DllImport(\"user32.dll\")]",
            "File.WriteAllText(p, s);",
            "File.Delete(p);",
            "db.SaveAs(p, DwgVersion.Current);",
            "Registry.CurrentUser.CreateSubKey(k);",
            "var s = new PipeSecurity();",
        ];

        foreach (var rule in Rules)
        {
            var regex = new Regex(rule.Pattern);
            Assert.True(bad.Any(b => regex.IsMatch(RepoFiles.CodeOnly(b))), $"Правило {rule.Id} не ловит ни одного примера — паттерн сломан.");
        }
    }
}
