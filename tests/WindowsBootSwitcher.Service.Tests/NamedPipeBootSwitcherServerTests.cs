using System.Buffers;
using System.Collections.Immutable;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using WindowsBootSwitcher.Contracts.Responses;
using WindowsBootSwitcher.Contracts.Serialization;
using WindowsBootSwitcher.Service.Boot;
using WindowsBootSwitcher.Service.Diagnostics;
using WindowsBootSwitcher.Service.Ipc;
using WindowsBootSwitcher.Service.Security;
using Xunit;

namespace WindowsBootSwitcher.Service.Tests;

/// <summary>
/// End to end coverage of the named pipe protocol over a real pipe. The caller identity is stubbed
/// so the tests assert the authorization policy rather than the identity of whoever runs them.
/// </summary>
public sealed class NamedPipeBootSwitcherServerTests
{
    private static readonly CallerIdentity Administrator = new(IsLocalInteractiveUser: true, IsLocalAdministrator: true);
    private static readonly CallerIdentity StandardUser = new(IsLocalInteractiveUser: true, IsLocalAdministrator: false);
    private static readonly CallerIdentity Unprivileged = new(IsLocalInteractiveUser: false, IsLocalAdministrator: false);

    [Fact]
    public async Task Responds_to_get_state_with_the_current_boot_state()
    {
        await WithServerAsync(Administrator, async (pipeName, token) =>
        {
            var response = await SendAsync(pipeName, """{"command":"get_state","payload":{}}""", token);

            Assert.True(response.Success, $"{response.ErrorCode}: {response.ErrorMessage}");
            Assert.NotNull(response.State);
            Assert.Equal("entry-1", response.State!.CurrentDefaultEntryId);
            Assert.Equal(30, response.State.TimeoutSeconds);
            Assert.Equal(2, response.State.Entries.Length);
        });
    }

    [Fact]
    public async Task Rejects_unknown_commands()
    {
        await WithServerAsync(Administrator, async (pipeName, token) =>
        {
            var response = await SendAsync(pipeName, """{"command":"drop_tables","payload":{}}""", token);

            Assert.False(response.Success);
            Assert.Equal("unknown_command", response.ErrorCode);
        });
    }

    [Fact]
    public async Task Rejects_requests_without_a_command_name()
    {
        await WithServerAsync(Administrator, async (pipeName, token) =>
        {
            var response = await SendAsync(pipeName, """{"payload":{}}""", token);

            Assert.False(response.Success);
            Assert.Equal("invalid_request", response.ErrorCode);
        });
    }

    [Fact]
    public async Task Rejects_malformed_json()
    {
        await WithServerAsync(Administrator, async (pipeName, token) =>
        {
            var response = await SendAsync(pipeName, "{ this is not json", token);

            Assert.False(response.Success);
            Assert.Equal("invalid_request", response.ErrorCode);
        });
    }

    [Fact]
    public async Task Rejects_requests_larger_than_the_limit_with_a_dedicated_code()
    {
        await WithServerAsync(Administrator, async (pipeName, token) =>
        {
            var oversizedId = new string('a', NamedPipeBootSwitcherServer.MaxRequestBytes + 4096);
            var request = "{\"command\":\"set_default_entry\",\"payload\":{\"EntryId\":\"" + oversizedId + "\"}}";

            var response = await SendAsync(pipeName, request, token);

            Assert.False(response.Success);
            Assert.Equal("request_too_large", response.ErrorCode);
        });
    }

    [Fact]
    public async Task Denies_mutation_to_callers_who_are_not_administrators()
    {
        await WithServerAsync(StandardUser, async (pipeName, token) =>
        {
            var response = await SendAsync(pipeName, """{"command":"set_timeout","payload":{"Mode":"Off"}}""", token);

            Assert.False(response.Success);
            Assert.Equal("access_denied", response.ErrorCode);
        });
    }

    [Fact]
    public async Task Allows_standard_users_to_read_state()
    {
        await WithServerAsync(StandardUser, async (pipeName, token) =>
        {
            var response = await SendAsync(pipeName, """{"command":"get_state","payload":{}}""", token);

            Assert.True(response.Success);
        });
    }

    [Fact]
    public async Task Denies_reads_to_callers_who_are_neither_interactive_nor_administrators()
    {
        await WithServerAsync(Unprivileged, async (pipeName, token) =>
        {
            var response = await SendAsync(pipeName, """{"command":"get_state","payload":{}}""", token);

            Assert.False(response.Success);
            Assert.Equal("access_denied", response.ErrorCode);
        });
    }

    [Fact]
    public async Task Keeps_serving_after_a_client_disconnects_without_reading_the_response()
    {
        await WithServerAsync(Administrator, async (pipeName, token) =>
        {
            // A client that vanishes mid-exchange must not fault the accept loop.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                using var abandoning = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await abandoning.ConnectAsync(10000, token);
                abandoning.ReadMode = PipeTransmissionMode.Message;
                var payload = Encoding.UTF8.GetBytes("""{"command":"get_state","payload":{}}""");
                await abandoning.WriteAsync(payload, token);
                await abandoning.FlushAsync(token);
            }

            var response = await SendAsync(pipeName, """{"command":"get_state","payload":{}}""", token);
            Assert.True(response.Success);
        });
    }

    [Fact]
    public async Task Serves_multiple_clients_concurrently()
    {
        await WithServerAsync(Administrator, async (pipeName, token) =>
        {
            var requests = Enumerable.Range(0, 8)
                .Select(_ => SendAsync(pipeName, """{"command":"get_state","payload":{}}""", token))
                .ToArray();

            var responses = await Task.WhenAll(requests);

            Assert.All(responses, response => Assert.True(response.Success));
        });
    }

    private static async Task WithServerAsync(CallerIdentity caller, Func<string, CancellationToken, Task> body)
    {
        var pipeName = "wbs-test-" + Guid.NewGuid().ToString("N");
        var adapter = new StubAdapter();
        var router = new BootCommandRouter(new BootConfigurationService(adapter), new CallerAuthorizationPolicy());
        using var eventLogWriter = new EventLogWriter();
        var server = new NamedPipeBootSwitcherServer(router, new FixedIdentityInspector(caller), eventLogWriter, pipeName);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var serverTask = server.RunAsync(cts.Token);

        try
        {
            await body(pipeName, cts.Token);
        }
        finally
        {
            await cts.CancelAsync();
            await serverTask;
        }
    }

    private static async Task<BootOperationResponse> SendAsync(string pipeName, string request, CancellationToken cancellationToken)
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(15000, cancellationToken);
        client.ReadMode = PipeTransmissionMode.Message;

        var payload = Encoding.UTF8.GetBytes(request);

        // Write concurrently: an oversized request is abandoned by the server part way through, so
        // a synchronous write would block forever waiting for a reader.
        var writeTask = WriteAsync(client, payload, cancellationToken);
        var responseJson = await ReadMessageAsync(client, cancellationToken);

        try
        {
            await writeTask;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }

        Assert.False(string.IsNullOrEmpty(responseJson), "The server closed the pipe without responding.");

        return JsonSerializer.Deserialize(responseJson, ContractsJsonContext.Default.BootOperationResponse)
            ?? throw new InvalidOperationException("The server response could not be deserialized.");
    }

    private static async Task WriteAsync(NamedPipeClientStream pipe, byte[] payload, CancellationToken cancellationToken)
    {
        await pipe.WriteAsync(payload, cancellationToken);
        await pipe.FlushAsync(cancellationToken);
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

    private sealed class FixedIdentityInspector(CallerIdentity identity) : WindowsIdentityInspector
    {
        public override CallerIdentity Inspect(WindowsIdentity windowsIdentity) => identity;
    }

    private sealed class StubAdapter : IBootConfigurationAdapter
    {
        public BootConfigurationSnapshot ReadState() => new(
            CurrentDefaultEntryId: "entry-1",
            TimeoutSeconds: 30,
            Entries: ImmutableArray.Create(
                new BootConfigurationEntry("entry-1", "Windows 11", true),
                new BootConfigurationEntry("entry-2", "Windows 10", true)));

        public void SetDefaultEntry(string entryId)
        {
        }

        public void SetTimeout(int timeoutSeconds)
        {
        }
    }
}
