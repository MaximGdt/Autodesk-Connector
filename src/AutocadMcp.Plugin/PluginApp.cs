using AutocadMcp.Core.Audit;
using AutocadMcp.Core.Settings;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(AutocadMcp.Plugin.PluginApp))]
[assembly: CommandClass(typeof(AutocadMcp.Plugin.Commands))]

namespace AutocadMcp.Plugin;

/// <summary>
/// Entry point. AutoCAD calls Initialize on its main thread when the plugin loads
/// (at startup via the bundle, or via NETLOAD). The connector starts listening at once;
/// MCPSTOP turns it off for this session.
/// </summary>
public sealed class PluginApp : IExtensionApplication
{
    internal static PipeHost? Host { get; private set; }

    internal static ConnectorSettings Settings { get; private set; } = new();

    internal static AuditLog? Audit { get; private set; }

    internal static string? SettingsWarning { get; private set; }

    private static MainThreadDispatcher? _dispatcher;

    public void Initialize()
    {
        try
        {
            Settings = ConnectorSettings.Load(null, out var warning);
            SettingsWarning = warning;
            Audit = AuditLog.ForCurrentUser();
            _dispatcher = new MainThreadDispatcher();
            Host = new PipeHost(_dispatcher, new ToolRouter(Settings), Audit, Settings);
            Host.Start();

            Write("\n[Claude] Коннектор AutoCAD MCP загружен и ждёт подключения. Состояние: MCPSTATUS, выключить: MCPSTOP.\n");
            if (warning is not null)
            {
                Write($"[Claude] Внимание: {warning}\n");
            }
        }
        catch (System.Exception ex)
        {
            Write($"\n[Claude] Коннектор не запустился: {ex.Message}\n");
        }
    }

    public void Terminate()
    {
        Host?.Dispose();
        _dispatcher?.Dispose();
    }

    internal static void Write(string message)
    {
        try
        {
            AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(message);
        }
        catch (System.Exception)
        {
            // No document yet at startup: nothing to print to.
        }
    }
}
