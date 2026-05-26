using WindowsBootSwitcher.Contracts;
using WindowsBootSwitcher.Contracts.Responses;

namespace WindowsBootSwitcher.Tray.Client;

public interface IBootSwitchClient
{
    Task<BootOperationResponse> GetStateAsync(CancellationToken cancellationToken);

    Task<BootOperationResponse> SetDefaultEntryAsync(string entryId, CancellationToken cancellationToken);

    Task<BootOperationResponse> SetTimeoutAsync(BootMenuTimeoutMode mode, CancellationToken cancellationToken);
}
