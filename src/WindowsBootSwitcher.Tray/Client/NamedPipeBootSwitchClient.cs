using System.Buffers;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using WindowsBootSwitcher.Contracts;
using WindowsBootSwitcher.Contracts.Requests;
using WindowsBootSwitcher.Contracts.Responses;
using WindowsBootSwitcher.Contracts.Serialization;

namespace WindowsBootSwitcher.Tray.Client;

public sealed class NamedPipeBootSwitchClient : BootSwitchClient
{
    private const string PipeName = "WindowsBootSwitcher";
    private const int ConnectTimeoutMilliseconds = 250;
    private const int MaxConnectAttempts = 6;
    private const int InitialBackoffMilliseconds = 100;
    private const int MaxBackoffMilliseconds = 1000;
    private readonly string _pipeName;

    public NamedPipeBootSwitchClient(string pipeName = PipeName)
    {
        _pipeName = string.IsNullOrWhiteSpace(pipeName) ? PipeName : pipeName;
    }

    public Task<BootOperationResponse> GetStateAsync(CancellationToken cancellationToken)
        => SendAsync("get_state", new GetStateRequest(), ContractsJsonContext.Default.GetStateRequest, cancellationToken);

    public Task<BootOperationResponse> SetDefaultEntryAsync(string entryId, CancellationToken cancellationToken)
        => SendAsync("set_default_entry", new SetDefaultEntryRequest(entryId), ContractsJsonContext.Default.SetDefaultEntryRequest, cancellationToken);

    public Task<BootOperationResponse> SetTimeoutAsync(BootMenuTimeoutMode mode, CancellationToken cancellationToken)
        => SendAsync("set_timeout", new SetTimeoutRequest(mode), ContractsJsonContext.Default.SetTimeoutRequest, cancellationToken);

    private async Task<BootOperationResponse> SendAsync<TRequest>(
        string command,
        TRequest payload,
        JsonTypeInfo<TRequest> payloadTypeInfo,
        CancellationToken cancellationToken)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName: _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await ConnectWithRetryAsync(pipe, cancellationToken).ConfigureAwait(false);

        pipe.ReadMode = PipeTransmissionMode.Message;
        await WriteRequestAsync(pipe, command, payload, payloadTypeInfo, cancellationToken).ConfigureAwait(false);

        using var responseDocument = await ReadResponseAsync(pipe, cancellationToken).ConfigureAwait(false);
        var response = JsonSerializer.Deserialize(responseDocument.RootElement, ContractsJsonContext.Default.BootOperationResponse);
        return response ?? new BootOperationResponse(false, "invalid_response", "The service returned an invalid response.", null);
    }

    private static async Task ConnectWithRetryAsync(NamedPipeClientStream pipe, CancellationToken cancellationToken)
    {
        var backoffMilliseconds = InitialBackoffMilliseconds;

        for (var attempt = 1; attempt <= MaxConnectAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await pipe.ConnectAsync(ConnectTimeoutMilliseconds, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (TimeoutException) when (attempt < MaxConnectAttempts)
            {
            }
            catch (IOException) when (attempt < MaxConnectAttempts)
            {
            }
            catch (TimeoutException exception)
            {
                throw new IOException("Unable to connect to the Windows Boot Switcher service after multiple attempts.", exception);
            }
            catch (IOException exception)
            {
                throw new IOException("Unable to connect to the Windows Boot Switcher service after multiple attempts.", exception);
            }

            await Task.Delay(backoffMilliseconds, cancellationToken).ConfigureAwait(false);
            backoffMilliseconds = Math.Min(backoffMilliseconds * 2, MaxBackoffMilliseconds);
        }
    }

    private static async Task WriteRequestAsync<TRequest>(
        NamedPipeClientStream pipe,
        string command,
        TRequest payload,
        JsonTypeInfo<TRequest> payloadTypeInfo,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("command", command);
            writer.WritePropertyName("payload");
            JsonSerializer.Serialize(writer, payload, payloadTypeInfo);
            writer.WriteEndObject();
            writer.Flush();
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(pipe, cancellationToken).ConfigureAwait(false);
        await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonDocument> ReadResponseAsync(NamedPipeClientStream pipe, CancellationToken cancellationToken)
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
                    break;
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
}
