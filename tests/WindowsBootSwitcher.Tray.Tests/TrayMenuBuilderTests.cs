using System.Collections.Immutable;
using System.Linq;
using WindowsBootSwitcher.Contracts;
using WindowsBootSwitcher.Tray;
using Xunit;

namespace WindowsBootSwitcher.Tray.Tests;

public sealed class TrayMenuBuilderTests
{
    [Fact]
    public void Build_connected_state_renders_the_default_header_entries_timeout_and_app_commands()
    {
        var bootState = new BootState(
            CurrentDefaultEntryId: "entry-2",
            TimeoutSeconds: 30,
            Entries: ImmutableArray.Create(
                new BootEntry("entry-1", "Linux", false),
                new BootEntry("entry-2", "Windows 11", true)));

        var model = TrayMenuBuilder.Build(TrayState.Available(bootState));

        Assert.Equal(7, model.Items.Length);
        Assert.True(model.Items[0].IsDisabled);
        Assert.Equal("Current default: Windows 11", model.Items[0].Text);

        Assert.Equal("Linux", model.Items[1].Text);
        Assert.True(model.Items[1].IsEnabled);
        Assert.Equal(TrayMenuCommandKind.SetDefaultEntry, model.Items[1].CommandKind);
        Assert.Equal("entry-1", model.Items[1].CommandArgument);

        Assert.Equal("Windows 11", model.Items[2].Text);
        Assert.True(model.Items[2].IsEnabled);
        Assert.True(model.Items[2].IsChecked);

        Assert.Equal("Timeout", model.Items[3].Text);
        Assert.True(model.Items[3].IsEnabled);
        Assert.Equal(2, model.Items[3].Children.Length);
        Assert.Equal("Off", model.Items[3].Children[0].Text);
        Assert.False(model.Items[3].Children[0].IsChecked);
        Assert.Equal("30 seconds", model.Items[3].Children[1].Text);
        Assert.True(model.Items[3].Children[1].IsChecked);

        Assert.True(model.Items[4].IsSeparator);
        Assert.Equal("Refresh", model.Items[5].Text);
        Assert.Equal(TrayMenuCommandKind.Refresh, model.Items[5].CommandKind);
        Assert.Equal("Exit", model.Items[6].Text);
        Assert.Equal(TrayMenuCommandKind.Exit, model.Items[6].CommandKind);
    }

    [Fact]
    public void Build_unavailable_state_shows_status_and_disables_mutation_actions()
    {
        var bootState = new BootState(
            CurrentDefaultEntryId: "entry-1",
            TimeoutSeconds: 30,
            Entries: ImmutableArray.Create(
                new BootEntry("entry-1", "Windows 11", true),
                new BootEntry("entry-2", "Linux", false)));

        var model = TrayMenuBuilder.Build(TrayState.UnavailableRetrying("Service unavailable. Retrying...", bootState));

        Assert.Equal("Service unavailable. Retrying...", model.Items[0].Text);
        Assert.True(model.Items[0].IsDisabled);
        Assert.True(model.Items[1].IsDisabled);
        Assert.True(model.Items[2].IsDisabled);
        Assert.True(model.Items[3].IsDisabled);
        Assert.True(model.Items[3].Children.All(child => child.IsDisabled));
        Assert.True(model.Items[4].IsDisabled);
        Assert.True(model.Items[5].IsSeparator);
        Assert.True(model.Items[6].IsEnabled);
        Assert.True(model.Items[7].IsEnabled);
    }
}
