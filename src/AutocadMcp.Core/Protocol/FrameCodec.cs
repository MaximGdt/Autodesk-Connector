using System.Buffers.Binary;
using System.Text;

namespace AutocadMcp.Core.Protocol;

/// <summary>
/// Length-prefixed framing: 4-byte little-endian length, then UTF-8 JSON.
/// Oversized or empty frames are rejected before any allocation of the payload,
/// so a bad client cannot make the plugin allocate gigabytes.
/// </summary>
public static class FrameCodec
{
    /// <summary>Hard ceiling for one message (requests and responses).</summary>
    public const int MaxFrameBytes = 4 * 1024 * 1024;

    public static async Task WriteAsync(Stream stream, string json, CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        if (payload.Length == 0 || payload.Length > MaxFrameBytes)
        {
            throw new FrameException($"Сообщение слишком большое или пустое: {payload.Length} байт (максимум {MaxFrameBytes}).");
        }

        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Reads one frame. Returns null when the other side closed the connection cleanly.</summary>
    public static async Task<string?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        if (!await ReadExactAsync(stream, header, ct).ConfigureAwait(false))
        {
            return null;
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaxFrameBytes)
        {
            throw new FrameException($"Недопустимая длина сообщения: {length} байт.");
        }

        var payload = new byte[length];
        if (!await ReadExactAsync(stream, payload, ct).ConfigureAwait(false))
        {
            throw new FrameException("Соединение оборвалось посреди сообщения.");
        }

        return Encoding.UTF8.GetString(payload);
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
            if (n == 0)
            {
                return read == 0 ? false : throw new FrameException("Соединение оборвалось посреди сообщения.");
            }

            read += n;
        }

        return true;
    }
}

public sealed class FrameException(string message) : Exception(message);
