using System.Buffers;
using System.Collections.Immutable;
using System.IO.Pipes;
using System.Text.Json;
using WindowsBootSwitcher.Contracts;
using WindowsBootSwitcher.Contracts.Responses;
using WindowsBootSwitcher.Contracts.Serialization;
using WindowsBootSwitcher.Tray.Client;
using Xunit;

namespace WindowsBootSwitcher.Tray.Tests;

public sealed class NamedPipeBootSwitchClientTests
{
    [Fact]
    public async Task GetStateAsync_throws_after_retry_exhaustion()
    {
        var client = new NamedPipeBootSwitchClient(pipeName: "missing-pipe-" + Guid.NewGuid());

        await Assert.ThrowsAsync<IOException>(() => client.GetStateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetStateAsync_sends_get_state_command_and_reads_response()
    {
        var pipeName = "tray-test-" + Guid.NewGuid();
        var expectedResponse = CreateSuccessResponse(timeoutSeconds: 30);
        var requestJson = await RunServerClientRoundTripAsync(
            pipeName,
            expectedResponse,
            client => client.GetStateAsync(CancellationToken.None));

        using var requestDocument = JsonDocument.Parse(requestJson);
        Assert.Equal("get_state", requestDocument.RootElement.GetProperty("command").GetString());
        Assert.Equal(JsonValueKind.Object, requestDocument.RootElement.GetProperty("payload").ValueKind);
    }

    [Fact]
    public async Task SetTimeoutAsync_sends_mode_payload_and_reads_response()
    {
        var pipeName = "tray-test-" + Guid.NewGuid();
        var expectedResponse = CreateSuccessResponse(timeoutSeconds: 0);
        var requestJson = await RunServerClientRoundTripAsync(
            pipeName,
            expectedResponse,
            client => client.SetTimeoutAsync(BootMenuTimeoutMode.Off, CancellationToken.None));

        using var requestDocument = JsonDocument.Parse(requestJson);
        Assert.Equal("set_timeout", requestDocument.RootElement.GetProperty("command").GetString());
        Assert.Equal("Off", requestDocument.RootElement.GetProperty("payload").GetProperty("Mode").GetString());
    }

    private static BootOperationResponse CreateSuccessResponse(int timeoutSeconds) =>
        new(
            Success: true,
            ErrorCode: null,
            ErrorMessage: null,
            State: new BootState(
                CurrentDefaultEntryId: "entry-1",
                TimeoutSeconds: timeoutSeconds,
                Entries: ImmutableArray.Create(
                    new BootEntry("entry-1", "Windows 11", true),
                    new BootEntry("entry-2", "Windows 10", true))));

    private static async Task<string> RunServerClientRoundTripAsync(
        string pipeName,
        BootOperationResponse response,
        Func<NamedPipeBootSwitchClient, Task<BootOperationResponse>> clientRequest)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Message,
            PipeOptions.Asynchronous);

        string? requestJson = null;
        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(cts.Token);
            requestJson = await ReadMessageAsync(server, cts.Token);
            await JsonSerializer.SerializeAsync(server, response, ContractsJsonContext.Default.BootOperationResponse, cts.Token);
            await server.FlushAsync(cts.Token);
        }, cts.Token);

        var client = new NamedPipeBootSwitchClient(pipeName);
        var clientResponse = await clientRequest(client);

        Assert.True(clientResponse.Success);
        Assert.NotNull(clientResponse.State);
        Assert.Equal(response.State?.TimeoutSeconds, clientResponse.State?.TimeoutSeconds);

        await serverTask;
        return requestJson ?? throw new InvalidOperationException("Expected the client request payload.");
    }

    private static async Task<string> ReadMessageAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        var memory = new MemoryStream();
        var rented = ArrayPool<byte>.Shared.Rent(4096);

        try
        {
            while (true)
            {
                var bytesRead = await pipe.ReadAsync(rented.AsMemory(0, rented.Length), cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                memory.Write(rented, 0, bytesRead);
                if (pipe.IsMessageComplete)
                {
                    break;
                }
            }

            return System.Text.Encoding.UTF8.GetString(memory.ToArray());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
            memory.Dispose();
        }
    }
}
