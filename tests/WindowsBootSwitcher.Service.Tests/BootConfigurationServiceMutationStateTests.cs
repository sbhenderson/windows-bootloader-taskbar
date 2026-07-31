using System.Collections.Immutable;
using WindowsBootSwitcher.Contracts.Requests;
using WindowsBootSwitcher.Service.Boot;
using Xunit;

namespace WindowsBootSwitcher.Service.Tests;

/// <summary>
/// Regression coverage for state returned alongside a successful mutation.
/// </summary>
public sealed class BootConfigurationServiceMutationStateTests
{
    [Fact]
    public void SetDefaultEntry_marks_the_new_entry_as_default_in_the_returned_state()
    {
        // The snapshot still says entry-2 is the default; the response must not echo that stale
        // flag, because the tray picks the checked entry from BootEntry.IsDefault.
        var adapter = new RecordingAdapter(
            new BootConfigurationSnapshot(
                CurrentDefaultEntryId: "entry-2",
                TimeoutSeconds: 30,
                Entries: ImmutableArray.Create(
                    new BootConfigurationEntry("entry-2", "Windows 11", true),
                    new BootConfigurationEntry("entry-3", "Windows Recovery", true))));

        var response = new BootConfigurationService(adapter).SetDefaultEntry(new SetDefaultEntryRequest("entry-3"));

        Assert.True(response.Success);
        Assert.NotNull(response.State);
        Assert.Equal("entry-3", response.State!.CurrentDefaultEntryId);

        var newDefault = Assert.Single(response.State.Entries, entry => entry.IsDefault);
        Assert.Equal("entry-3", newDefault.Id);

        var previousDefault = Assert.Single(response.State.Entries, entry => entry.Id == "entry-2");
        Assert.False(previousDefault.IsDefault);
    }

    [Fact]
    public void SetDefaultEntry_is_case_insensitive_about_the_entry_id()
    {
        var adapter = new RecordingAdapter(
            new BootConfigurationSnapshot(
                CurrentDefaultEntryId: "{AAAA}",
                TimeoutSeconds: 5,
                Entries: ImmutableArray.Create(
                    new BootConfigurationEntry("{AAAA}", "Windows 11", true),
                    new BootConfigurationEntry("{BBBB}", "Windows 10", true))));

        var response = new BootConfigurationService(adapter).SetDefaultEntry(new SetDefaultEntryRequest("{bbbb}"));

        Assert.True(response.Success);
        var newDefault = Assert.Single(response.State!.Entries, entry => entry.IsDefault);
        Assert.Equal("{BBBB}", newDefault.Id);
    }

    [Fact]
    public void SetTimeout_keeps_the_existing_default_entry_flag()
    {
        var adapter = new RecordingAdapter(
            new BootConfigurationSnapshot(
                CurrentDefaultEntryId: "entry-2",
                TimeoutSeconds: 30,
                Entries: ImmutableArray.Create(
                    new BootConfigurationEntry("entry-2", "Windows 11", true),
                    new BootConfigurationEntry("entry-3", "Windows Recovery", true))));

        var response = new BootConfigurationService(adapter).SetTimeout(new SetTimeoutRequest(Contracts.BootMenuTimeoutMode.Off));

        Assert.True(response.Success);
        Assert.Equal(0, response.State!.TimeoutSeconds);
        var currentDefault = Assert.Single(response.State.Entries, entry => entry.IsDefault);
        Assert.Equal("entry-2", currentDefault.Id);
    }

    private sealed class RecordingAdapter(BootConfigurationSnapshot snapshot) : IBootConfigurationAdapter
    {
        public BootConfigurationSnapshot ReadState() => snapshot;

        public void SetDefaultEntry(string entryId)
        {
        }

        public void SetTimeout(int timeoutSeconds)
        {
        }
    }
}
