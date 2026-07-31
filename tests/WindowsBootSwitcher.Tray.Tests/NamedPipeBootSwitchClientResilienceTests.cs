using System.Buffers;
using System.Collections.Immutable;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using WindowsBootSwitcher.Contracts;
using WindowsBootSwitcher.Contracts.Responses;
using WindowsBootSwitcher.Contracts.Serialization;
using WindowsBootSwitcher.Tray.Client;
using Xunit;

namespace WindowsBootSwitcher.Tray.Tests;

/// <summary>
/// Covers how the client behaves when the service misbehaves rather than when it answers cleanly.
/// </summary>
public sealed class NamedPipeBootSwitchClientResilienceTests
{
    [Fact]
    public async Task Request_times_out_when_the_service_accepts_but_never_responds()
    {
        var pipeName = "tray-test-" + Guid.NewGuid();
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Message,
            PipeOptions.Asynchronous);

        var acceptTask = server.WaitForConnectionAsync();

        // Without a request timeout this would hang until the tray exits.
        var client = new NamedPipeBootSwitchClient(pipeName, TimeSpan.FromMilliseconds(750));

        await Assert.ThrowsAsync<TimeoutException>(() => client.GetStateAsync(CancellationToken.None));

        await IgnoreAsync(acceptTask);
    }

    [Fact]
    public async Task Caller_cancellation_surfaces_as_cancellation_not_timeout()
    {
        var pipeName = "tray-test-" + Guid.NewGuid();
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Message,
            PipeOptions.Asynchronous);

        var acceptTask = server.WaitForConnectionAsync();
        var client = new NamedPipeBootSwitchClient(pipeName, TimeSpan.FromSeconds(30));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetStateAsync(cts.Token));

        await IgnoreAsync(acceptTask);
    }

    [Fact]
    public async Task Malformed_response_becomes_an_invalid_response_rather_than_a_crash()
    {
        var response = await RunAsync(
            respond: async (pipe, token) =>
            {
                var garbage = Encoding.UTF8.GetBytes("{ not json at all");
                await pipe.WriteAsync(garbage, token);
                await pipe.FlushAsync(token);
            },
            request: client => client.GetStateAsync(CancellationToken.None));

        Assert.False(response.Success);
        Assert.Equal("invalid_response", response.ErrorCode);
    }

    [Fact]
    public async Task Null_response_body_becomes_an_invalid_response()
    {
        var response = await RunAsync(
            respond: async (pipe, token) =>
            {
                // Valid JSON that deserializes to no response object at all.
                var nullLiteral = Encoding.UTF8.GetBytes("null");
                await pipe.WriteAsync(nullLiteral, token);
                await pipe.FlushAsync(token);
            },
            request: client => client.GetStateAsync(CancellationToken.None));

        Assert.False(response.Success);
        Assert.Equal("invalid_response", response.ErrorCode);
    }

    [Fact]
    public async Task SetDefaultEntryAsync_sends_the_entry_id_payload()
    {
        string? capturedRequest = null;

        var response = await RunAsync(
            respond: async (pipe, token) =>
            {
                await JsonSerializer.SerializeAsync(
                    pipe,
                    new BootOperationResponse(true, null, null, CreateState()),
                    ContractsJsonContext.Default.BootOperationResponse,
                    token);
                await pipe.FlushAsync(token);
            },
            request: client => client.SetDefaultEntryAsync("{entry-9}", CancellationToken.None),
            captureRequest: json => capturedRequest = json);

        Assert.True(response.Success);
        Assert.NotNull(capturedRequest);

        using var document = JsonDocument.Parse(capturedRequest!);
        Assert.Equal("set_default_entry", document.RootElement.GetProperty("command").GetString());
        Assert.Equal("{entry-9}", document.RootElement.GetProperty("payload").GetProperty("EntryId").GetString());
    }

    [Fact]
    public async Task Service_failures_are_returned_with_their_error_code()
    {
        var response = await RunAsync(
            respond: async (pipe, token) =>
            {
                await JsonSerializer.SerializeAsync(
                    pipe,
                    new BootOperationResponse(false, "access_denied", "Nope.", null),
                    ContractsJsonContext.Default.BootOperationResponse,
                    token);
                await pipe.FlushAsync(token);
            },
            request: client => client.GetStateAsync(CancellationToken.None));

        Assert.False(response.Success);
        Assert.Equal("access_denied", response.ErrorCode);
        Assert.Equal("Nope.", response.ErrorMessage);
    }

    private static BootState CreateState() => new(
        CurrentDefaultEntryId: "{entry-9}",
        TimeoutSeconds: 30,
        Entries: ImmutableArray.Create(new BootEntry("{entry-9}", "Windows 11", true)));

    private static async Task<BootOperationResponse> RunAsync(
        Func<NamedPipeServerStream, CancellationToken, Task> respond,
        Func<NamedPipeBootSwitchClient, Task<BootOperationResponse>> request,
        Action<string>? captureRequest = null)
    {
        var pipeName = "tray-test-" + Guid.NewGuid();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Message,
            PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(cts.Token);
            var requestJson = await ReadMessageAsync(server, cts.Token);
            captureRequest?.Invoke(requestJson);
            await respond(server, cts.Token);
        }, cts.Token);

        var client = new NamedPipeBootSwitchClient(pipeName, TimeSpan.FromSeconds(10));
        var response = await request(client);

        await serverTask;
        return response;
    }

    private static async Task<string> ReadMessageAsync(PipeStream pipe, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
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

                buffer.Write(rented, 0, bytesRead);
                if (pipe.IsMessageComplete)
                {
                    break;
                }
            }

            return Encoding.UTF8.GetString(buffer.ToArray());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
            buffer.Dispose();
        }
    }

    private static async Task IgnoreAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }
}
