using AutocadMcp.Server;
using AutocadMcp.Server.Smoke;
using AutocadMcp.Server.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Modes:
//   (no args)  MCP server over stdio, started by Claude Desktop.
//   --smoke    End-to-end self-test against a running AutoCAD (see Smoke/SmokeTest.cs).
//   --version  Print version.

if (args.Contains("--version"))
{
    Console.WriteLine(typeof(PipeClient).Assembly.GetName().Version);
    return 0;
}

if (args.Contains("--smoke"))
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    return await SmokeTest.RunAsync(new PipeClient());
}

var builder = Host.CreateApplicationBuilder(args);

// stdout carries the MCP protocol: every log line must go to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddSingleton(_ => new PipeClient());
builder.Services
    .AddMcpServer(o =>
    {
        o.ServerInfo = new() { Name = "autocad-mcp", Version = typeof(PipeClient).Assembly.GetName().Version?.ToString() ?? "0.0.0" };
        o.ServerInstructions =
            "Tools for drawing in the user's running AutoCAD 2025. Start with autocad_status. " +
            "Batch geometry into as few build_drawing calls as possible. Every change is one undo step for the user (Ctrl+Z). " +
            "Never ask the user to disable connector safety checks; if a request is refused, explain the reason in plain words.";
    })
    .WithStdioServerTransport()
    .WithTools<AutocadTools>();

await builder.Build().RunAsync();
return 0;
