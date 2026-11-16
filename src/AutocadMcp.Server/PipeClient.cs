using System.IO.Pipes;
using System.Text.Json;
using AutocadMcp.Core;
using AutocadMcp.Core.Protocol;
using AutocadMcp.Core.Settings;

namespace AutocadMcp.Server;

/// <summary>A failure reported by the plugin or by the connection, with a stable code.</summary>
public sealed class ConnectorException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Sends one request at a time to the AutoCAD plugin. Connects per request
/// (cheap for local pipes) so a restarted AutoCAD is picked up automatically.
/// </summary>
public sealed class PipeClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _pipeName;
    private readonly TimeSpan _requestTimeout;

    public PipeClient(string? pipeName = null, ConnectorSettings? settings = null)
    {
        _pipeName = pipeName ?? PipeNames.ForCurrentUser();
        var s = settings ?? ConnectorSettings.Load(null, out _);
        // A little longer than the plugin's own timeout, so the plugin's answer wins.
        _requestTimeout = TimeSpan.FromSeconds(s.RequestTimeoutSeconds + 10);
    }

    public async Task<JsonElement> CallAsync(string tool, object? args, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_requestTimeout);

            await using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            try
            {
                await pipe.ConnectAsync(ConnectTimeout, timeout.Token).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new ConnectorException(ErrorCodes.NotConnected,
                    "Нет связи с AutoCAD. Проверь: AutoCAD 2025 запущен, коннектор включён (команда MCPSTATUS в AutoCAD).");
            }

            var request = new PipeRequest(PipeRequest.CurrentProtocol, Guid.NewGuid().ToString("N"), tool,
                JsonWire.ToElement(args ?? new { }));

            try
            {
                await FrameCodec.WriteAsync(pipe, JsonWire.Serialize(request), timeout.Token).ConfigureAwait(false);
                var json = await FrameCodec.ReadAsync(pipe, timeout.Token).ConfigureAwait(false)
                    ?? throw new ConnectorException(ErrorCodes.NotConnected, "AutoCAD закрыл соединение, не ответив.");

                var response = JsonWire.Deserialize<PipeResponse>(json)
                    ?? throw new ConnectorException(ErrorCodes.BadRequest, "Пустой ответ от AutoCAD.");

                if (!response.Ok)
                {
                    throw new ConnectorException(response.Error?.Code ?? ErrorCodes.Internal, response.Error?.Message ?? "Неизвестная ошибка.");
                }

                return response.Result ?? JsonWire.ToElement(new { });
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new ConnectorException(ErrorCodes.Timeout, "AutoCAD не ответил вовремя.");
            }
            catch (IOException ex)
            {
                throw new ConnectorException(ErrorCodes.NotConnected, $"Связь с AutoCAD оборвалась: {ex.Message}");
            }
            catch (FrameException ex)
            {
                throw new ConnectorException(ErrorCodes.BadRequest, ex.Message);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
