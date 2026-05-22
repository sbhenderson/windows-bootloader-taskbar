using System.Collections.Immutable;
using WindowsBootSwitcher.Service.Boot;
using Xunit;

namespace WindowsBootSwitcher.Service.Tests;

public sealed class BootEntryFilterTests
{
    [Fact]
    public void Filter_keeps_only_windows_os_loader_entries_and_marks_the_default_entry()
    {
        var filter = new BootEntryFilter();
        var snapshot = new BootConfigurationSnapshot(
            CurrentDefaultEntryId: "entry-2",
            TimeoutSeconds: 30,
            Entries: ImmutableArray.Create(
                new BootConfigurationEntry("entry-1", "Linux", false),
                new BootConfigurationEntry("entry-2", "Windows 11", true),
                new BootConfigurationEntry("entry-3", "Windows Recovery", true)));

        var entries = filter.Filter(snapshot);

        Assert.Equal(2, entries.Length);
        Assert.Equal("entry-2", entries[0].Id);
        Assert.Equal("Windows 11", entries[0].DisplayName);
        Assert.True(entries[0].IsDefault);
        Assert.Equal("entry-3", entries[1].Id);
        Assert.False(entries[1].IsDefault);
    }
}
