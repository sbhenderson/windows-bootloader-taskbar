using Microsoft.Extensions.Hosting;
using WindowsBootSwitcher.Service.Diagnostics;
using WindowsBootSwitcher.Service.Ipc;

namespace WindowsBootSwitcher.Service.Hosting;

public sealed class WindowsBootSwitcherWorker : BackgroundService
{
    private readonly NamedPipeBootSwitcherServer _server;
    private readonly EventLogWriter _eventLogWriter;

    public WindowsBootSwitcherWorker(NamedPipeBootSwitcherServer server, EventLogWriter eventLogWriter)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _eventLogWriter = eventLogWriter ?? throw new ArgumentNullException(nameof(eventLogWriter));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _server.RunAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _eventLogWriter.WriteError("The named-pipe IPC host stopped unexpectedly.", exception);
            throw;
        }
    }
}
