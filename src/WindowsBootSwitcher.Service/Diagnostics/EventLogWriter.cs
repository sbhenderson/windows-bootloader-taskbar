using System.Diagnostics;

namespace WindowsBootSwitcher.Service.Diagnostics;

public sealed class EventLogWriter : IDisposable
{
    private const string LogName = "Application";
    private const string SourceName = "WindowsBootSwitcher.Service";

    /// <summary>
    /// The Windows Event Log rejects entries longer than 32766 characters; stay well below it.
    /// </summary>
    private const int MaxMessageLength = 16000;

    private readonly EventLog _eventLog;

    public EventLogWriter()
    {
        EnsureSourceExists();
        _eventLog = new EventLog(LogName)
        {
            Source = SourceName
        };
    }

    /// <summary>
    /// Truncates and escapes untrusted text (such as a client supplied command name) so that it
    /// cannot be used to flood or spoof event log entries.
    /// </summary>
    public static string Sanitize(string? value, int maxLength = 128)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(Math.Min(value.Length, maxLength));
        foreach (var character in value)
        {
            if (builder.Length >= maxLength)
            {
                builder.Append("...");
                break;
            }

            builder.Append(char.IsControl(character) ? '?' : character);
        }

        return builder.ToString();
    }

    public void WriteInformation(string message) => WriteEntry(message, EventLogEntryType.Information);

    public void WriteWarning(string message) => WriteEntry(message, EventLogEntryType.Warning);

    public void WriteWarning(string message, Exception exception) => WriteEntry(FormatMessage(message, exception), EventLogEntryType.Warning);

    public void WriteError(string message, Exception exception) => WriteEntry(FormatMessage(message, exception), EventLogEntryType.Error);

    public void Dispose()
    {
        _eventLog.Dispose();
    }

    private static string FormatMessage(string message, Exception exception) =>
        $"{message} {exception}";

    /// <summary>
    /// Diagnostics must never take the service down, so event log failures are swallowed.
    /// </summary>
    private void WriteEntry(string message, EventLogEntryType type)
    {
        try
        {
            if (message.Length > MaxMessageLength)
            {
                message = string.Concat(message.AsSpan(0, MaxMessageLength), "... (truncated)");
            }

            _eventLog.WriteEntry(message, type, 1000);
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException
            or System.ComponentModel.Win32Exception
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or ObjectDisposedException)
        {
        }
    }

    private static void EnsureSourceExists()
    {
        try
        {
            if (EventLog.SourceExists(SourceName))
            {
                return;
            }

            EventLog.CreateEventSource(new EventSourceCreationData(SourceName, LogName));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
        {
        }
    }
}
