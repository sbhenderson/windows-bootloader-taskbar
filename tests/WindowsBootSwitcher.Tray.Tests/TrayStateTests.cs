using System.Collections.Immutable;
using WindowsBootSwitcher.Contracts;
using WindowsBootSwitcher.Tray;
using Xunit;

namespace WindowsBootSwitcher.Tray.Tests;

public sealed class TrayStateTests
{
    [Fact]
    public void Connecting_creates_a_connecting_state_without_boot_state()
    {
        var state = TrayState.Connecting();

        Assert.Equal(TrayConnectionStatus.Connecting, state.ConnectionStatus);
        Assert.Null(state.BootState);
        Assert.False(state.HasBootState);
        Assert.False(state.IsAvailable);
    }

    [Fact]
    public void Available_wraps_the_boot_state_and_marks_the_tray_available()
    {
        var bootState = new BootState(
            CurrentDefaultEntryId: "entry-1",
            TimeoutSeconds: 30,
            Entries: ImmutableArray.Create(new BootEntry("entry-1", "Windows 11", true)));

        var state = TrayState.Available(bootState);

        Assert.Equal(TrayConnectionStatus.Available, state.ConnectionStatus);
        Assert.Same(bootState, state.BootState);
        Assert.True(state.HasBootState);
        Assert.True(state.IsAvailable);
        Assert.Null(state.StatusMessage);
    }

    [Fact]
    public void UnavailableRetrying_can_retain_the_last_known_boot_state()
    {
        var bootState = new BootState(
            CurrentDefaultEntryId: "entry-1",
            TimeoutSeconds: 30,
            Entries: ImmutableArray.Create(new BootEntry("entry-1", "Windows 11", true)));

        var state = TrayState.UnavailableRetrying("Service unavailable. Retrying...", bootState);

        Assert.Equal(TrayConnectionStatus.UnavailableRetrying, state.ConnectionStatus);
        Assert.Equal("Service unavailable. Retrying...", state.StatusMessage);
        Assert.Same(bootState, state.BootState);
        Assert.True(state.HasBootState);
        Assert.False(state.IsAvailable);
    }
}
