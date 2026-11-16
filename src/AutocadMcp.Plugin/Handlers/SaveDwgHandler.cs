using System.Text.Json;
using AutocadMcp.Core.Protocol;
using AutocadMcp.Core.Safety;
using Autodesk.AutoCAD.DatabaseServices;

namespace AutocadMcp.Plugin.Handlers;

/// <summary>
/// save_dwg: saves a COPY of the open drawing as DWG into the allowed output folder.
/// The path always comes from PathGuard (docs/SECURITY.md, S5). This is the only
/// place in the plugin allowed to call SaveAs (checked by SecurityTests).
/// </summary>
internal static class SaveDwgHandler
{
    internal sealed class SaveArgs
    {
        public string? FileName { get; set; }
        public string? Version { get; set; }
        public bool Overwrite { get; set; }
    }

    private static readonly Dictionary<string, DwgVersion> Versions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["2018"] = DwgVersion.AC1032,
        ["2013"] = DwgVersion.AC1027,
        ["2010"] = DwgVersion.AC1024,
        ["2007"] = DwgVersion.AC1021,
        ["2004"] = DwgVersion.AC1800,
        ["2000"] = DwgVersion.AC1015,
    };

    public static object Handle(JsonElement args, ToolContext ctx)
    {
        var a = Args.Parse<SaveArgs>(args);

        if (!PathGuard.TryResolveDwgPath(ctx.Settings.OutputFolder, a.FileName, out var path, out var error))
        {
            throw new HandlerException(ErrorCodes.PathDenied, $"Файл не сохранён: {error}. Папка для сохранения: {ctx.Settings.OutputFolder}");
        }

        var versionKey = string.IsNullOrWhiteSpace(a.Version) ? "2018" : a.Version.Trim();
        if (!Versions.TryGetValue(versionKey, out var version))
        {
            throw new HandlerException(ErrorCodes.ValidationFailed, $"Версия DWG «{a.Version}» не поддерживается. Допустимо: {string.Join(", ", Versions.Keys)}.");
        }

        if (string.Equals(Path.GetFullPath(ctx.Document.Name), path, StringComparison.OrdinalIgnoreCase))
        {
            throw new HandlerException(ErrorCodes.PathDenied, "Это файл, открытый сейчас в AutoCAD. Чтобы сохранить его, нажми Ctrl+S.");
        }

        if (File.Exists(path) && !a.Overwrite)
        {
            throw new HandlerException(ErrorCodes.AlreadyExists, $"Файл {Path.GetFileName(path)} уже есть. Выбери другое имя или явно разреши перезапись (overwrite).");
        }

        Directory.CreateDirectory(ctx.Settings.OutputFolder);
        ctx.Document.Database.SaveAs(path, version);

        return new
        {
            path,
            version = versionKey,
            bytes = new FileInfo(path).Length,
        };
    }
}
