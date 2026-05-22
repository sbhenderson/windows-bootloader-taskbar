using System.Buffers;
using System.Collections.Immutable;
using System.IO.Pipes;
using System.Text.Json;
using WindowsBootSwitcher.Contracts;
using WindowsBootSwitcher.Contracts.Requests;
using WindowsBootSwitcher.Contracts.Responses;
using WindowsBootSwitcher.Contracts.Serialization;
using WindowsBootSwitcher.Tray.Client;
using Xunit;

namespace WindowsBootSwitcher.Tray.Tests;

public sealed class NamedPipeBootSwitchClientTests
{
    [Fact]
    public async Task GetStateAsync_sends_the_get_state_command_and_reads_the_response()
    {
        var pipeName = $"WindowsBootSwitcher-{Guid.NewGuid():N}";
        var ready = new TaskCompletionSource();

        var serverTask = Task.Run(async () =>
        {
            using var server = CreateServer(pipeName);
            ready.SetResult();
            server.WaitForConnection();

            using var requestDocument = await ReadMessageAsync(server, CancellationToken.None);
            Assert.Equal("get_state", requestDocument.RootElement.GetProperty("command").GetString());
            Assert.Equal(JsonValueKind.Object, requestDocument.RootElement.GetProperty("payload").ValueKind);

            var response = new BootOperationResponse(
                Success: true,
                ErrorCode: null,
                ErrorMessage: null,
                State: new BootState(
                    CurrentDefaultEntryId: "entry-1",
                    TimeoutSeconds: 30,
                    Entries: ImmutableArray.Create(new BootEntry("entry-1", "Windows 11", true))));

            await JsonSerializer.SerializeAsync(server, response, ContractsJsonContext.Default.BootOperationResponse);
            await server.FlushAsync();
        });

        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var client = new NamedPipeBootSwitchClient(pipeName);
        var response = await client.GetStateAsync(CancellationToken.None);
        await serverTask;

        Assert.True(response.Success);
        Assert.NotNull(response.State);
        Assert.Equal("entry-1", response.State!.CurrentDefaultEntryId);
        Assert.Equal(30, response.State.TimeoutSeconds);
    }

    [Fact]
    public async Task SetTimeoutAsync_sends_the_timeout_request_with_the_expected_payload()
    {
        var pipeName = $"WindowsBootSwitcher-{Guid.NewGuid():N}";
        var ready = new TaskCompletionSource();

        var serverTask = Task.Run(async () =>
        {
            using var server = CreateServer(pipeName);
            ready.SetResult();
            server.WaitForConnection();

            using var requestDocument = await ReadMessageAsync(server, CancellationToken.None);
            Assert.Equal("set_timeout", requestDocument.RootElement.GetProperty("command").GetString());

            var payload = requestDocument.RootElement.GetProperty("payload");
            Assert.Equal(JsonValueKind.String, payload.GetProperty("Mode").ValueKind);
            Assert.Equal("ThirtySeconds", payload.GetProperty("Mode").GetString());

            var response = new BootOperationResponse(
                Success: true,
                ErrorCode: null,
                ErrorMessage: null,
                State: new BootState(
                    CurrentDefaultEntryId: "entry-1",
                    TimeoutSeconds: 30,
                    Entries: ImmutableArray.Create(new BootEntry("entry-1", "Windows 11", true))));

            await JsonSerializer.SerializeAsync(server, response, ContractsJsonContext.Default.BootOperationResponse);
            await server.FlushAsync();
        });

        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var client = new NamedPipeBootSwitchClient(pipeName);
        var response = await client.SetTimeoutAsync(BootMenuTimeoutMode.ThirtySeconds, CancellationToken.None);
        await serverTask;

        Assert.True(response.Success);
        Assert.NotNull(response.State);
        Assert.Equal(30, response.State!.TimeoutSeconds);
    }

    private static NamedPipeServerStream CreateServer(string pipeName) =>
        new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);

    private static async Task<JsonDocument> ReadMessageAsync(PipeStream pipe, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        var rentedBuffer = ArrayPool<byte>.Shared.Rent(1024);

        try
        {
            while (true)
            {
                var bytesRead = await pipe.ReadAsync(rentedBuffer.AsMemory(0, rentedBuffer.Length), cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                buffer.Write(rentedBuffer, 0, bytesRead);
                if (pipe.IsMessageComplete)
                {
                    break;
                }
            }

            buffer.Position = 0;
            return await JsonDocument.ParseAsync(buffer, cancellationToken: cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
            buffer.Dispose();
        }
    }
}
