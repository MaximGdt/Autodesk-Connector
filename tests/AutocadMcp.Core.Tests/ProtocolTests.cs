using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
using AutocadMcp.Core.Protocol;
using AutocadMcp.Server;

namespace AutocadMcp.Core.Tests;

public class ProtocolTests
{
    [Fact]
    public async Task Frame_RoundTrips_Unicode()
    {
        using var ms = new MemoryStream();
        await FrameCodec.WriteAsync(ms, "{\"t\":\"Чертёж 📐\"}", CancellationToken.None);
        ms.Position = 0;
        Assert.Equal("{\"t\":\"Чертёж 📐\"}", await FrameCodec.ReadAsync(ms, CancellationToken.None));
        Assert.Null(await FrameCodec.ReadAsync(ms, CancellationToken.None)); // clean end of stream
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(FrameCodec.MaxFrameBytes + 1)]
    public async Task Frame_WithBadLength_IsRejectedBeforeAllocating(int length)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, length);
        using var ms = new MemoryStream(header);
        await Assert.ThrowsAsync<FrameException>(() => FrameCodec.ReadAsync(ms, CancellationToken.None));
    }

    [Fact]
    public async Task Frame_TruncatedPayload_IsRejected()
    {
        var bytes = new byte[4 + 3];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 10);
        using var ms = new MemoryStream(bytes);
        await Assert.ThrowsAsync<FrameException>(() => FrameCodec.ReadAsync(ms, CancellationToken.None));
    }

    [Fact]
    public async Task OversizedMessage_IsNotSent()
    {
        using var ms = new MemoryStream();
        var huge = new string('x', FrameCodec.MaxFrameBytes + 1);
        await Assert.ThrowsAsync<FrameException>(() => FrameCodec.WriteAsync(ms, huge, CancellationToken.None));
        Assert.Equal(0, ms.Length);
    }

    /// <summary>Full round-trip through a real named pipe with a fake plugin.</summary>
    [Fact]
    public async Task PipeClient_TalksToFakePlugin()
    {
        var name = "AutocadMcp.test." + Guid.NewGuid().ToString("N");
        var fake = Task.Run(async () =>
        {
            for (var i = 0; i < 2; i++)
            {
                await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync();
                var req = JsonWire.Deserialize<PipeRequest>((await FrameCodec.ReadAsync(server, CancellationToken.None))!)!;
                Assert.Equal(PipeRequest.CurrentProtocol, req.Protocol);

                var resp = req.Tool == "autocad_status"
                    ? PipeResponse.Success(req.Id, JsonWire.ToElement(new { connected = true }))
                    : PipeResponse.Failure(req.Id, ErrorCodes.UnknownTool, "нельзя");
                await FrameCodec.WriteAsync(server, JsonWire.Serialize(resp), CancellationToken.None);
            }
        });

        var client = new PipeClient(name);
        var ok = await client.CallAsync("autocad_status", null);
        Assert.True(ok.GetProperty("connected").GetBoolean());

        var ex = await Assert.ThrowsAsync<ConnectorException>(() => client.CallAsync("run_lisp", new { code = "x" }));
        Assert.Equal(ErrorCodes.UnknownTool, ex.Code);

        await fake;
    }

    [Fact]
    public async Task PipeClient_WithoutAutocad_SaysNotConnected()
    {
        var client = new PipeClient("AutocadMcp.nobody." + Guid.NewGuid().ToString("N"));
        var ex = await Assert.ThrowsAsync<ConnectorException>(() => client.CallAsync("autocad_status", null));
        Assert.Equal(ErrorCodes.NotConnected, ex.Code);
    }

    [Fact]
    public void PipeName_IsSafeAndPerUser()
    {
        var name = PipeNames.ForCurrentUser();
        Assert.Matches("^AutocadMcp\\.v1\\.[A-Za-z0-9_.-]+\\.[0-9]+$", name);
        Assert.Equal("____.a-b", PipeNames.Sanitize("Иван.a-b"));
    }

    [Fact]
    public void Json_RejectsTrailingGarbageAndDeepNesting()
    {
        Assert.ThrowsAny<JsonException>(() => JsonWire.Deserialize<PipeRequest>("{\"protocol\":1,}"));
        var deep = new string('[', 100) + new string(']', 100);
        Assert.ThrowsAny<JsonException>(() => JsonWire.Deserialize<JsonElement>(deep));
    }
}
