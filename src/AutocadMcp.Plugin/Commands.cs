using Autodesk.AutoCAD.Runtime;

namespace AutocadMcp.Plugin;

/// <summary>
/// Commands a person types in AutoCAD. MCPSTOP is the emergency switch:
/// Claude loses access immediately, until MCPSTART or the next AutoCAD start.
/// </summary>
public sealed class Commands
{
    [CommandMethod("MCPSTATUS")]
    public void Status()
    {
        var host = PluginApp.Host;
        var state = host is { IsRunning: true } ? "РАБОТАЕТ (Claude может подключиться)" : "ВЫКЛЮЧЕН";
        PluginApp.Write(
            $"\n[Claude] Коннектор: {state}" +
            $"\n  Канал: {host?.PipeName}" +
            $"\n  Запросов с запуска: {host?.RequestCount ?? 0}" +
            $"\n  Последняя ошибка: {host?.LastError ?? "нет"}" +
            $"\n  Папка сохранения DWG: {PluginApp.Settings.OutputFolder}" +
            $"\n  Журнал: {PluginApp.Audit?.Folder}" +
            (PluginApp.SettingsWarning is null ? "" : $"\n  Настройки: {PluginApp.SettingsWarning}") +
            "\n");
    }

    [CommandMethod("MCPSTOP")]
    public void Stop()
    {
        PluginApp.Host?.Stop();
        PluginApp.Write("\n[Claude] Коннектор ВЫКЛЮЧЕН. Claude больше не может менять чертёж. Включить снова: MCPSTART.\n");
    }

    [CommandMethod("MCPSTART")]
    public void Start()
    {
        var host = PluginApp.Host;
        host?.Start();
        PluginApp.Write(host is { IsRunning: true }
            ? "\n[Claude] Коннектор включён.\n"
            : $"\n[Claude] Коннектор не включился: {host?.LastError ?? "надстройка не загружена"}\n");
    }

    [CommandMethod("MCPLOG")]
    public void Log()
    {
        PluginApp.Write($"\n[Claude] Журнал запросов (по одному файлу на день): {PluginApp.Audit?.Folder}\n");
    }
}
