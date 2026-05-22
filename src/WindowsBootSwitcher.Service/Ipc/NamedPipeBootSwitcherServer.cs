using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using WindowsBootSwitcher.Contracts.Responses;
using WindowsBootSwitcher.Service.Diagnostics;
using WindowsBootSwitcher.Service.Security;

namespace WindowsBootSwitcher.Service.Ipc;

public sealed class NamedPipeBootSwitcherServer
{
    private const string PipeName = "WindowsBootSwitcher";
    private const int MaxRequestBytes = 64 * 1024;
    private readonly BootCommandRouter _router;
    private readonly WindowsIdentityInspector _identityInspector;
    private readonly EventLogWriter _eventLogWriter;

    public NamedPipeBootSwitcherServer(
        BootCommandRouter router,
        WindowsIdentityInspector identityInspector,
        EventLogWriter eventLogWriter)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _identityInspector = identityInspector ?? throw new ArgumentNullException(nameof(identityInspector));
        _eventLogWriter = eventLogWriter ?? throw new ArgumentNullException(nameof(eventLogWriter));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var inFlight = new List<Task>();

        while (!cancellationToken.IsCancellationRequested)
        {
            using var pipe = CreatePipe();

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                inFlight.Add(HandleClientAsync(pipe, cancellationToken));
                inFlight.RemoveAll(task => task.IsCompleted);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }

        await Task.WhenAll(inFlight).ConfigureAwait(false);
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        try
        {
            if (!IsLocalClient(pipe))
            {
                _eventLogWriter.WriteWarning("Rejected a remote IPC client.");
                await WriteResponseAsync(pipe, new BootOperationResponse(false, "remote_client_rejected", "Remote clients are not allowed.", null), cancellationToken).ConfigureAwait(false);
                return;
            }

            using var requestDocument = await ReadRequestAsync(pipe, cancellationToken).ConfigureAwait(false);
            if (!TryGetCommandEnvelope(requestDocument.RootElement, out var commandName, out var payload))
            {
                await WriteResponseAsync(pipe, new BootOperationResponse(false, "invalid_request", "The request must contain a command name.", null), cancellationToken).ConfigureAwait(false);
                _eventLogWriter.WriteWarning("Rejected an IPC request without a command name.");
                return;
            }

            var callerIdentity = GetCallerIdentity(pipe);
            var response = _router.Route(commandName, payload, callerIdentity);
            await WriteResponseAsync(pipe, response, cancellationToken).ConfigureAwait(false);

            if (!response.Success)
            {
                _eventLogWriter.WriteWarning($"IPC command '{commandName}' failed: {response.ErrorCode} - {response.ErrorMessage}");
            }
        }
        catch (JsonException exception)
        {
            _eventLogWriter.WriteWarning("Rejected an invalid IPC request.", exception);
            await WriteResponseAsync(pipe, new BootOperationResponse(false, "invalid_request", "The request payload was not valid JSON.", null), cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException exception)
        {
            _eventLogWriter.WriteWarning("Rejected a truncated IPC request.", exception);
            await WriteResponseAsync(pipe, new BootOperationResponse(false, "invalid_request", "The request payload was truncated.", null), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _eventLogWriter.WriteError("Unexpected failure while processing an IPC request.", exception);
            await WriteResponseAsync(pipe, new BootOperationResponse(false, "internal_error", "The service encountered an unexpected error.", null), cancellationToken).ConfigureAwait(false);
        }
    }

    private CallerIdentity GetCallerIdentity(NamedPipeServerStream pipe)
    {
        WindowsIdentity? identity = null;
        pipe.RunAsClient(() => identity = WindowsIdentity.GetCurrent(true));

        if (identity is null)
        {
            throw new InvalidOperationException("Unable to inspect the caller identity.");
        }

        using (identity)
        {
            return _identityInspector.Inspect(identity);
        }
    }

    private static async Task<JsonDocument> ReadRequestAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        var rentedBuffer = ArrayPool<byte>.Shared.Rent(4096);

        try
        {
            while (true)
            {
                var bytesRead = await pipe.ReadAsync(rentedBuffer.AsMemory(0, rentedBuffer.Length), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    throw new EndOfStreamException("The client closed the pipe before sending a complete request.");
                }

                buffer.Write(rentedBuffer, 0, bytesRead);
                if (buffer.Length > MaxRequestBytes)
                {
                    throw new InvalidOperationException("The request payload exceeded the maximum allowed size.");
                }

                if (pipe.IsMessageComplete)
                {
                    break;
                }
            }

            buffer.Position = 0;
            return await JsonDocument.ParseAsync(buffer, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
            buffer.Dispose();
        }
    }

    private static bool TryGetCommandEnvelope(JsonElement rootElement, out string commandName, out JsonElement payload)
    {
        commandName = string.Empty;
        payload = default;

        if (rootElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!rootElement.TryGetProperty("command", out var commandElement) || commandElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        commandName = commandElement.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return false;
        }

        payload = rootElement.TryGetProperty("payload", out var payloadElement) ? payloadElement : default;
        return true;
    }

    private static async Task WriteResponseAsync(NamedPipeServerStream pipe, BootOperationResponse response, CancellationToken cancellationToken)
    {
        await JsonSerializer.SerializeAsync(pipe, response, WindowsBootSwitcher.Contracts.Serialization.ContractsJsonContext.Default.BootOperationResponse, cancellationToken).ConfigureAwait(false);
        await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static NamedPipeServerStream CreatePipe()
    {
        return new NamedPipeServerStream(
            PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Message,
            PipeOptions.Asynchronous,
            0,
            0);
    }

    private static bool IsLocalClient(NamedPipeServerStream pipe)
    {
        var clientComputerName = new System.Text.StringBuilder(256);
        if (!GetNamedPipeClientComputerName(pipe.SafePipeHandle, clientComputerName, (uint)clientComputerName.Capacity))
        {
            return false;
        }

        var remoteName = clientComputerName.ToString();
        var netbiosName = remoteName.Split('.')[0];
        return string.Equals(netbiosName, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetNamedPipeClientComputerName(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, System.Text.StringBuilder clientComputerName, uint clientComputerNameLength);
}
