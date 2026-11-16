namespace AutocadMcp.Plugin;

/// <summary>An expected, explainable failure. Its message goes back to Claude as is (Russian, short).</summary>
internal sealed class HandlerException(string code, string message) : System.Exception(message)
{
    public string Code { get; } = code;
}
