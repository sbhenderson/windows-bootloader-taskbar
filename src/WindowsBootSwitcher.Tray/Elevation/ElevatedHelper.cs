using WindowsBootSwitcher.Contracts;
using WindowsBootSwitcher.Contracts.Responses;
using WindowsBootSwitcher.Tray.Client;

namespace WindowsBootSwitcher.Tray.Elevation;

/// <summary>Runs inside the elevated copy: sends one mutation and returns an exit code.</summary>
internal static class ElevatedHelper
{
    public static int Run(IReadOnlyList<string> args, IBootSwitchClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (!ElevatedCommand.TryParse(args, out var command) || command is null)
        {
            return ElevatedCommand.InvalidArgumentsExitCode;
        }

        try
        {
            BootOperationResponse response = command.Kind == ElevatedCommandKind.SetDefaultEntry
                ? client.SetDefaultEntryAsync(command.Argument, CancellationToken.None).GetAwaiter().GetResult()
                : client.SetTimeoutAsync(Enum.Parse<BootMenuTimeoutMode>(command.Argument), CancellationToken.None).GetAwaiter().GetResult();
            return ElevatedCommand.ToExitCode(response);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException)
        {
            return ElevatedCommand.TransportFailureExitCode;
        }
    }
}
