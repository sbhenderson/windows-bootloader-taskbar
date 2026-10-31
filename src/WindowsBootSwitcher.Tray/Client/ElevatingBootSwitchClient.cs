using WindowsBootSwitcher.Contracts;
using WindowsBootSwitcher.Contracts.Responses;
using WindowsBootSwitcher.Tray.Elevation;

namespace WindowsBootSwitcher.Tray.Client;

/// <summary>
/// Reads go straight to the service. Mutations need an elevated token, so a non-elevated tray
/// performs each one through an elevated copy of itself (one UAC prompt per change).
/// </summary>
public sealed class ElevatingBootSwitchClient(IBootSwitchClient inner, IElevatedProcessLauncher launcher) : IBootSwitchClient
{
    private readonly IBootSwitchClient _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IElevatedProcessLauncher _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));

    public Task<BootOperationResponse> GetStateAsync(CancellationToken cancellationToken)
        => _inner.GetStateAsync(cancellationToken);

    public Task<BootOperationResponse> SetDefaultEntryAsync(string entryId, CancellationToken cancellationToken)
    {
        if (_launcher.IsCurrentProcessElevated)
        {
            return _inner.SetDefaultEntryAsync(entryId, cancellationToken);
        }

        if (!ElevatedCommand.IsSafeArgument(entryId))
        {
            return Task.FromResult(new BootOperationResponse(false, ElevatedCommand.InvalidEntryIdCode, "The boot entry identifier was not valid.", null));
        }

        return RunElevatedAsync(ElevatedCommand.SetDefault(entryId), cancellationToken);
    }

    public Task<BootOperationResponse> SetTimeoutAsync(BootMenuTimeoutMode mode, CancellationToken cancellationToken)
        => _launcher.IsCurrentProcessElevated
            ? _inner.SetTimeoutAsync(mode, cancellationToken)
            : RunElevatedAsync(ElevatedCommand.SetTimeout(mode), cancellationToken);

    private async Task<BootOperationResponse> RunElevatedAsync(ElevatedCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var exitCode = await _launcher.RunElevatedAsync(command.ToArguments(), cancellationToken).ConfigureAwait(true);
            return ElevatedCommand.FromExitCode(exitCode);
        }
        catch (ElevationCancelledException)
        {
            return new BootOperationResponse(false, ElevatedCommand.ElevationCancelledCode, "The change was cancelled.", null);
        }
    }
}
