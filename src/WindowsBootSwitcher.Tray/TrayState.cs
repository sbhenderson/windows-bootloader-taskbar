using WindowsBootSwitcher.Contracts;

namespace WindowsBootSwitcher.Tray;

public enum TrayConnectionStatus
{
    Connecting,
    Available,
    UnavailableRetrying
}

public sealed record TrayState(TrayConnectionStatus ConnectionStatus, string? StatusMessage, BootState? BootState)
{
    public bool HasBootState => BootState is not null;

    public bool IsAvailable => ConnectionStatus == TrayConnectionStatus.Available && BootState is not null;

    public static TrayState Connecting(string statusMessage = "Connecting to Windows Boot Switcher service...")
        => new(TrayConnectionStatus.Connecting, statusMessage, null);

    public static TrayState Available(BootState bootState)
    {
        ArgumentNullException.ThrowIfNull(bootState);
        return new TrayState(TrayConnectionStatus.Available, null, bootState);
    }

    public static TrayState UnavailableRetrying(string statusMessage, BootState? bootState = null)
        => new(TrayConnectionStatus.UnavailableRetrying, statusMessage, bootState);
}
