using System.Collections.Immutable;

namespace WindowsBootSwitcher.Service.Boot;

public interface IBootConfigurationAdapter
{
    BootConfigurationSnapshot ReadState();

    void SetDefaultEntry(string entryId);

    void SetTimeout(int timeoutSeconds);
}

public sealed record BootConfigurationSnapshot(
    string? CurrentDefaultEntryId,
    int TimeoutSeconds,
    ImmutableArray<BootConfigurationEntry> Entries);

public sealed record BootConfigurationEntry(
    string Id,
    string DisplayName,
    bool IsWindowsOsLoader);
