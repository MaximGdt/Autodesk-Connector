using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using AutocadMcp.Core;
using AutocadMcp.Core.Audit;
using AutocadMcp.Core.Protocol;
using AutocadMcp.Core.Settings;

namespace AutocadMcp.Plugin;

/// <summary>
/// Listens on a named pipe that only the current Windows user can open
/// (PipeOptions.CurrentUserOnly, docs/SECURITY.md S2). No TCP port, no network.
/// One client at a time; each request is checked, executed on the main thread,
/// answered and written to the audit log.
/// </summary>
internal sealed class PipeHost : IDisposable
{
    private readonly MainThreadDispatcher _dispatcher;
    private readonly ToolRouter _router;
    private readonly AuditLog _audit;
    private readonly ConnectorSettings _settings;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private Task? _stoppingLoop;
    private long _requestCount;

    public PipeHost(MainThreadDispatcher dispatcher, ToolRouter router, AuditLog audit, ConnectorSettings settings)
    {
        _dispatcher = dispatcher;
        _router = router;
        _audit = audit;
        _settings = settings;
        PipeName = PipeNames.ForCurrentUser();
    }

    public string PipeName { get; }

    public bool IsRunning => _loop is { IsCompleted: false };

    public long RequestCount => Interlocked.Read(ref _requestCount);

    public string? LastError { get; private set; }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        if (_stoppingLoop is { IsCompleted: false })
        {
            // The previous loop still holds the pipe; a new one would fail with "channel busy".
            LastError = "Предыдущая остановка ещё не завершилась. Подожди несколько секунд и повтори MCPSTART.";
            return;
        }

        LastError = null;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Stop()
    {
        var cts = _cts;
        var loop = _loop;
        cts?.Cancel();
        try
        {
            loop?.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
            // cancelled
        }

        // Dispose the token source only when nothing uses it any more.
        if (loop is null || loop.IsCompleted)
        {
            cts?.Dispose();
            _stoppingLoop = null;
        }
        else
        {
            _stoppingLoop = loop;
        }

        _cts = null;
        _loop = null;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (System.Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // With one allowed instance .NET asks Windows for the FIRST pipe instance,
                // so a second AutoCAD gets "access denied" (UnauthorizedAccessException), not IOException.
                LastError = "Канал уже занят: коннектор запущен в другом окне AutoCAD. Работает только одно окно AutoCAD за раз.";
                return;
            }

            await using (server)
            {
                try
                {
                    await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                    await ServeClientAsync(server, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (System.Exception ex) when (ex is IOException or FrameException)
                {
                    LastError = $"Соединение прервано: {ex.Message}";
                }
            }
        }
    }

    private async Task ServeClientAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        while (server.IsConnected && !ct.IsCancellationRequested)
        {
            var json = await FrameCodec.ReadAsync(server, ct).ConfigureAwait(false);
            if (json is null)
            {
                return; // client closed the connection
            }

            var response = await HandleAsync(json, ct).ConfigureAwait(false);

            // Answer even after MCPSTOP, so Claude hears "connector_stopped" instead of a broken pipe.
            using var writeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await FrameCodec.WriteAsync(server, JsonWire.Serialize(response), writeTimeout.Token).ConfigureAwait(false);
        }
    }

    private async Task<PipeResponse> HandleAsync(string json, CancellationToken ct)
    {
        Interlocked.Increment(ref _requestCount);
        var sw = Stopwatch.StartNew();
        PipeRequest? request = null;
        PipeResponse response;

        try
        {
            request = JsonWire.Deserialize<PipeRequest>(json);
            if (request is null || string.IsNullOrEmpty(request.Id) || string.IsNullOrEmpty(request.Tool))
            {
                response = PipeResponse.Failure(request?.Id ?? "", ErrorCodes.BadRequest, "Неполный запрос.");
            }
            else if (request.Protocol != PipeRequest.CurrentProtocol)
            {
                response = PipeResponse.Failure(request.Id, ErrorCodes.ProtocolMismatch,
                    $"Версии коннектора не совпадают (протокол {request.Protocol}, ожидается {PipeRequest.CurrentProtocol}). Переустанови обе части.");
            }
            else if (!ToolNames.All.Contains(request.Tool) || !_router.IsKnown(request.Tool))
            {
                response = PipeResponse.Failure(request.Id, ErrorCodes.UnknownTool, $"Действие «{request.Tool}» не разрешено.");
            }
            else
            {
                var timeout = TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds);
                var req = request;
                response = await _dispatcher.InvokeAsync(() => _router.Execute(req), timeout, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            response = PipeResponse.Failure(request?.Id ?? "", ErrorCodes.Stopped,
                "Коннектор выключен командой MCPSTOP, запрос не выполнен. Включить снова: MCPSTART в AutoCAD.");
        }
        catch (JsonException ex)
        {
            response = PipeResponse.Failure(request?.Id ?? "", ErrorCodes.BadRequest, $"Некорректный JSON: {ex.Message}");
        }
        catch (TimeoutException ex)
        {
            response = PipeResponse.Failure(request?.Id ?? "", ErrorCodes.Timeout, ex.Message);
        }
        catch (System.Exception ex)
        {
            response = PipeResponse.Failure(request?.Id ?? "", ErrorCodes.Internal, $"Внутренняя ошибка коннектора ({ex.GetType().Name}).");
        }

        sw.Stop();
        _audit.Write(new AuditEntry(
            DateTimeOffset.Now,
            request?.Id ?? "",
            request?.Tool ?? "?",
            response.Ok,
            response.Error?.Code,
            sw.ElapsedMilliseconds,
            json.Length,
            request is null ? null : request.Args.ToString()));

        return response;
    }

    public void Dispose() => Stop();
}
