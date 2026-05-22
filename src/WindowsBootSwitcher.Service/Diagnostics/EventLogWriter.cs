using System.Diagnostics;

namespace WindowsBootSwitcher.Service.Diagnostics;

public sealed class EventLogWriter : IDisposable
{
    private const string LogName = "Application";
    private const string SourceName = "WindowsBootSwitcher.Service";
    private readonly EventLog _eventLog;

    public EventLogWriter()
    {
        EnsureSourceExists();
        _eventLog = new EventLog(LogName)
        {
            Source = SourceName
        };
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

    private void WriteEntry(string message, EventLogEntryType type)
    {
        _eventLog.WriteEntry(message, type, 1000);
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
