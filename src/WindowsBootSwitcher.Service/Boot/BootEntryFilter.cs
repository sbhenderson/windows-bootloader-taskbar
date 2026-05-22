using System.Collections.Immutable;
using WindowsBootSwitcher.Contracts;

namespace WindowsBootSwitcher.Service.Boot;

public sealed class BootEntryFilter
{
    public ImmutableArray<BootEntry> Filter(BootConfigurationSnapshot snapshot)
    {
        var builder = ImmutableArray.CreateBuilder<BootEntry>();

        foreach (var entry in snapshot.Entries)
        {
            if (!entry.IsWindowsOsLoader)
            {
                continue;
            }

            builder.Add(new BootEntry(
                entry.Id,
                entry.DisplayName,
                string.Equals(entry.Id, snapshot.CurrentDefaultEntryId, StringComparison.OrdinalIgnoreCase)));
        }

        return builder.ToImmutable();
    }
}
