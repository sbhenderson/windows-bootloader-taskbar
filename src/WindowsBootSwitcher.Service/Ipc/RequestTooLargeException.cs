namespace WindowsBootSwitcher.Service.Ipc;

/// <summary>
/// Thrown when a client sends a request larger than the server is willing to buffer.
/// </summary>
public sealed class RequestTooLargeException(int maxRequestBytes)
    : Exception($"The request payload exceeded the maximum allowed size of {maxRequestBytes} bytes.")
{
    public int MaxRequestBytes { get; } = maxRequestBytes;
}
