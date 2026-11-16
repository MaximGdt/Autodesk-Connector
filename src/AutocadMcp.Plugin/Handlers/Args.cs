using System.Text.Json;
using AutocadMcp.Core;
using AutocadMcp.Core.Protocol;

namespace AutocadMcp.Plugin.Handlers;

internal static class Args
{
    /// <summary>Deserializes tool arguments or fails with a clear bad_request.</summary>
    public static T Parse<T>(JsonElement args) where T : class
    {
        if (args.ValueKind != JsonValueKind.Object)
        {
            throw new HandlerException(ErrorCodes.BadRequest, "Параметры должны быть JSON-объектом.");
        }

        return JsonWire.FromElement<T>(args)
            ?? throw new HandlerException(ErrorCodes.BadRequest, "Пустые параметры.");
    }

    public static HandlerException Invalid(IEnumerable<string> errors) =>
        new(ErrorCodes.ValidationFailed, "Запрос не прошёл проверку, ничего не изменено:\n- " + string.Join("\n- ", errors));
}
