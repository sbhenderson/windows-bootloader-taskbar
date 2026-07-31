using System.Buffers;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using WindowsBootSwitcher.Contracts.Responses;
using WindowsBootSwitcher.Contracts.Serialization;
using WindowsBootSwitcher.Service.Diagnostics;
using WindowsBootSwitcher.Service.Security;

namespace WindowsBootSwitcher.Service.Ipc;

public sealed class NamedPipeBootSwitcherServer
{
    internal const string DefaultPipeName = "WindowsBootSwitcher";
    internal const int MaxRequestBytes = 64 * 1024;

    /// <summary>ERROR_PIPE_LOCAL: the connected client is on this machine.</summary>
    private const int ErrorPipeLocal = 229;

    /// <summary>
    /// Caps how many clients are served at once so a burst of connections cannot exhaust thread
    /// pool threads or pipe instances.
    /// </summary>
    internal const int MaxConcurrentClients = 16;

    /// <summary>
    /// A client that connects but never completes a request must not hold a slot forever.
    /// </summary>
    internal static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(30);

    private readonly BootCommandRouter _router;
    private readonly WindowsIdentityInspector _identityInspector;
    private readonly EventLogWriter _eventLogWriter;
    private readonly string _pipeName;

    public NamedPipeBootSwitcherServer(
        BootCommandRouter router,
        WindowsIdentityInspector identityInspector,
        EventLogWriter eventLogWriter)
        : this(router, identityInspector, eventLogWriter, DefaultPipeName)
    {
    }

    internal NamedPipeBootSwitcherServer(
        BootCommandRouter router,
        WindowsIdentityInspector identityInspector,
        EventLogWriter eventLogWriter,
        string pipeName)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _identityInspector = identityInspector ?? throw new ArgumentNullException(nameof(identityInspector));
        _eventLogWriter = eventLogWriter ?? throw new ArgumentNullException(nameof(eventLogWriter));
        _pipeName = string.IsNullOrWhiteSpace(pipeName) ? DefaultPipeName : pipeName;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var inFlight = new List<Task>();
        using var clientSlots = new SemaphoreSlim(MaxConcurrentClients, MaxConcurrentClients);

        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            var slotAcquired = false;

            try
            {
                await clientSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
                slotAcquired = true;

                pipe = CreatePipe(_pipeName);
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                var acceptedPipe = pipe;
                pipe = null;
                slotAcquired = false;
                inFlight.Add(ServeClientAsync(acceptedPipe, clientSlots, cancellationToken));
                inFlight.RemoveAll(task => task.IsCompleted);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                pipe?.Dispose();
                if (slotAcquired)
                {
                    clientSlots.Release();
                }

                break;
            }
            catch (Exception exception)
            {
                pipe?.Dispose();
                if (slotAcquired)
                {
                    clientSlots.Release();
                }

                _eventLogWriter.WriteError("Failed while accepting an IPC client connection.", exception);

                // Back off so a persistent failure (such as a pipe name collision) cannot hot spin.
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        await Task.WhenAll(inFlight).ConfigureAwait(false);
    }

    /// <summary>
    /// Wraps a client exchange so it can never fault. A faulted handler would otherwise surface as
    /// an aggregate failure when shutdown awaits the outstanding handlers, taking the worker down.
    /// </summary>
    private async Task ServeClientAsync(NamedPipeServerStream pipe, SemaphoreSlim clientSlots, CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ClientTimeout);

            try
            {
                await HandleClientAsync(pipe, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _eventLogWriter.WriteWarning("Abandoned an IPC client that exceeded the request timeout.");
            }
            catch (OperationCanceledException)
            {
            }
        }
        catch (Exception exception)
        {
            _eventLogWriter.WriteError("Unhandled failure while serving an IPC client.", exception);
        }
        finally
        {
            clientSlots.Release();
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using (pipe)
        {
            try
            {
                if (!IsLocalClient(pipe))
                {
                    _eventLogWriter.WriteWarning("Rejected a remote IPC client.");
                    await TryWriteResponseAsync(pipe, new BootOperationResponse(false, "remote_client_rejected", "Remote clients are not allowed.", null), cancellationToken).ConfigureAwait(false);
                    return;
                }

                using var requestDocument = await ReadRequestAsync(pipe, cancellationToken).ConfigureAwait(false);
                if (!TryGetCommandEnvelope(requestDocument.RootElement, out var commandName, out var payload))
                {
                    await TryWriteResponseAsync(pipe, new BootOperationResponse(false, "invalid_request", "The request must contain a command name.", null), cancellationToken).ConfigureAwait(false);
                    _eventLogWriter.WriteWarning("Rejected an IPC request without a command name.");
                    return;
                }

                var callerIdentity = GetCallerIdentity(pipe);
                var response = _router.Route(commandName, payload, callerIdentity);
                await TryWriteResponseAsync(pipe, response, cancellationToken).ConfigureAwait(false);

                if (!response.Success)
                {
                    _eventLogWriter.WriteWarning(
                        $"IPC command '{EventLogWriter.Sanitize(commandName)}' failed: {response.ErrorCode} - {EventLogWriter.Sanitize(response.ErrorMessage, 512)}");
                }
            }
            catch (RequestTooLargeException exception)
            {
                _eventLogWriter.WriteWarning("Rejected an oversized IPC request.", exception);
                await TryWriteResponseAsync(pipe, new BootOperationResponse(false, "request_too_large", exception.Message, null), cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException exception)
            {
                _eventLogWriter.WriteWarning("Rejected an invalid IPC request.", exception);
                await TryWriteResponseAsync(pipe, new BootOperationResponse(false, "invalid_request", "The request payload was not valid JSON.", null), cancellationToken).ConfigureAwait(false);
            }
            catch (EndOfStreamException exception)
            {
                _eventLogWriter.WriteWarning("Rejected a truncated IPC request.", exception);
                await TryWriteResponseAsync(pipe, new BootOperationResponse(false, "invalid_request", "The request payload was truncated.", null), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _eventLogWriter.WriteError("Unexpected failure while processing an IPC request.", exception);
                await TryWriteResponseAsync(pipe, new BootOperationResponse(false, "internal_error", "The service encountered an unexpected error.", null), cancellationToken).ConfigureAwait(false);
            }
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

                // Enforce the cap before buffering so an oversized message is never materialised.
                if (buffer.Length + bytesRead > MaxRequestBytes)
                {
                    throw new RequestTooLargeException(MaxRequestBytes);
                }

                buffer.Write(rentedBuffer, 0, bytesRead);

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

    /// <summary>
    /// Best effort response write: a client that already disconnected must not turn the handler
    /// into a faulted task.
    /// </summary>
    private async Task TryWriteResponseAsync(NamedPipeServerStream pipe, BootOperationResponse response, CancellationToken cancellationToken)
    {
        try
        {
            if (!pipe.IsConnected)
            {
                return;
            }

            await JsonSerializer.SerializeAsync(pipe, response, ContractsJsonContext.Default.BootOperationResponse, cancellationToken).ConfigureAwait(false);
            await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException
            or ObjectDisposedException
            or InvalidOperationException
            or OperationCanceledException)
        {
            _eventLogWriter.WriteWarning("Unable to deliver an IPC response to a disconnected client.", exception);
        }
    }

    private static NamedPipeServerStream CreatePipe(string pipeName)
    {
        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Message,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            CreatePipeSecurity());
    }

    /// <summary>
    /// Applies an explicit least privilege DACL instead of inheriting the process default.
    /// Non-administrators still need read/write access because they are permitted to query state;
    /// <see cref="CallerAuthorizationPolicy"/> remains the authority on what a caller may do.
    /// </summary>
    internal static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize,
            AccessControlType.Allow));

        // Tokens produced by a network logon carry the NETWORK SID; local logons do not.
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Deny));

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AnonymousSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Deny));

        return security;
    }

    /// <summary>
    /// Determines whether the connected client is on this machine.
    /// </summary>
    /// <remarks>
    /// Windows reports a local client by <em>failing</em> this call with ERROR_PIPE_LOCAL: there is
    /// no remote computer name to return. Treating failure as "remote" therefore rejects every
    /// legitimate local caller, so the error code must be inspected.
    /// </remarks>
    private static bool IsLocalClient(NamedPipeServerStream pipe)
    {
        var clientComputerName = new char[256];
        if (!GetNamedPipeClientComputerName(
                pipe.SafePipeHandle,
                clientComputerName,
                (uint)(clientComputerName.Length * sizeof(char))))
        {
            return Marshal.GetLastPInvokeError() == ErrorPipeLocal;
        }

        var rawName = new string(clientComputerName);
        var terminatorIndex = rawName.IndexOf('\0', StringComparison.Ordinal);
        if (terminatorIndex >= 0)
        {
            rawName = rawName[..terminatorIndex];
        }

        if (string.IsNullOrWhiteSpace(rawName))
        {
            return false;
        }

        var remoteName = NormalizeComputerName(rawName);
        var netbiosName = remoteName.Split('.')[0];
        return string.Equals(netbiosName, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeComputerName(string rawClientName)
    {
        var normalized = rawClientName.Trim();
        while (normalized.StartsWith(@"\\", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        if (string.Equals(normalized, ".", StringComparison.Ordinal) ||
            string.Equals(normalized, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return Environment.MachineName;
        }

        return normalized;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientComputerName(
        Microsoft.Win32.SafeHandles.SafePipeHandle pipe,
        [Out] char[] clientComputerName,
        uint clientComputerNameLength);
}
