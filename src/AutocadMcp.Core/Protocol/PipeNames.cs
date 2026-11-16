using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AutocadMcp.Core.Protocol;

/// <summary>
/// The pipe name is per Windows user and per logon session, so two people on one
/// terminal server never reach each other's AutoCAD. Access is additionally restricted
/// to the current user by PipeOptions.CurrentUserOnly on both ends (see docs/SECURITY.md, S2).
/// </summary>
public static partial class PipeNames
{
    public static string ForCurrentUser()
    {
        var user = Sanitize(Environment.UserName);
        var session = Process.GetCurrentProcess().SessionId;
        return $"AutocadMcp.v{PipeRequest.CurrentProtocol}.{user}.{session}";
    }

    public static string Sanitize(string value)
    {
        var cleaned = SafeChars().Replace(value, "_");
        return cleaned.Length > 64 ? cleaned[..64] : cleaned;
    }

    [GeneratedRegex("[^A-Za-z0-9_.-]")]
    private static partial Regex SafeChars();
}
