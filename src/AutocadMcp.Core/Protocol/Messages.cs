using System.Text.Json;

namespace AutocadMcp.Core.Protocol;

/// <summary>A request sent from the MCP server to the AutoCAD plugin over the named pipe.</summary>
public sealed record PipeRequest(int Protocol, string Id, string Tool, JsonElement Args)
{
    public const int CurrentProtocol = 1;
}

/// <summary>The plugin's answer. Exactly one of Result / Error is set.</summary>
public sealed record PipeResponse(string Id, bool Ok, JsonElement? Result, PipeError? Error)
{
    public static PipeResponse Success(string id, JsonElement result) => new(id, true, result, null);

    public static PipeResponse Failure(string id, string code, string message) =>
        new(id, false, null, new PipeError(code, message));
}

/// <summary>Error with a stable machine code and a short human message (Russian, shown to the user via Claude).</summary>
public sealed record PipeError(string Code, string Message);

/// <summary>Stable error codes. Claude and the smoke test rely on these values.</summary>
public static class ErrorCodes
{
    public const string BadRequest = "bad_request";
    public const string ProtocolMismatch = "protocol_mismatch";
    public const string UnknownTool = "unknown_tool";
    public const string ValidationFailed = "validation_failed";
    public const string NoDocument = "no_document";
    public const string Busy = "autocad_busy";
    public const string Stopped = "connector_stopped";
    public const string Timeout = "timeout";
    public const string PathDenied = "path_denied";
    public const string AlreadyExists = "already_exists";
    public const string NotConnected = "not_connected";
    public const string Internal = "internal_error";
}
