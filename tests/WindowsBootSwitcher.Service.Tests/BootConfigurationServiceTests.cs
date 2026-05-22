using System.Collections.Immutable;
using WindowsBootSwitcher.Contracts;
using WindowsBootSwitcher.Contracts.Requests;
using WindowsBootSwitcher.Service.Boot;
using Xunit;

namespace WindowsBootSwitcher.Service.Tests;

public sealed class BootConfigurationServiceTests
{
    [Fact]
    public void GetState_filters_entries_and_returns_the_current_default_entry()
    {
        var adapter = new FakeBootConfigurationAdapter(
            new BootConfigurationSnapshot(
                CurrentDefaultEntryId: "entry-2",
                TimeoutSeconds: 30,
                Entries: ImmutableArray.Create(
                    new BootConfigurationEntry("entry-1", "Linux", false),
                    new BootConfigurationEntry("entry-2", "Windows 11", true),
                    new BootConfigurationEntry("entry-3", "Windows Recovery", true))));

        var service = new BootConfigurationService(adapter);

        var state = service.GetState();

        Assert.Equal("entry-2", state.CurrentDefaultEntryId);
        Assert.Equal(30, state.TimeoutSeconds);
        Assert.Equal(2, state.Entries.Length);
        Assert.Equal("entry-2", state.Entries[0].Id);
        Assert.True(state.Entries[0].IsDefault);
        Assert.Equal("entry-3", state.Entries[1].Id);
        Assert.False(state.Entries[1].IsDefault);
    }

    [Fact]
    public void SetDefaultEntry_updates_the_adapter_and_returns_the_updated_state()
    {
        var adapter = new FakeBootConfigurationAdapter(
            new BootConfigurationSnapshot(
                CurrentDefaultEntryId: "entry-2",
                TimeoutSeconds: 30,
                Entries: ImmutableArray.Create(
                    new BootConfigurationEntry("entry-2", "Windows 11", true),
                    new BootConfigurationEntry("entry-3", "Windows Recovery", true))));

        var service = new BootConfigurationService(adapter);

        var response = service.SetDefaultEntry(new SetDefaultEntryRequest("entry-3"));

        Assert.True(response.Success);
        Assert.Null(response.ErrorCode);
        Assert.Null(response.ErrorMessage);
        Assert.NotNull(response.State);
        Assert.Equal("entry-3", response.State!.CurrentDefaultEntryId);
        Assert.Equal("entry-3", adapter.DefaultEntryId);
    }

    [Fact]
    public void SetDefaultEntry_returns_a_failure_response_when_the_entry_is_missing()
    {
        var adapter = new FakeBootConfigurationAdapter(
            new BootConfigurationSnapshot(
                CurrentDefaultEntryId: "entry-2",
                TimeoutSeconds: 30,
                Entries: ImmutableArray.Create(
                    new BootConfigurationEntry("entry-2", "Windows 11", true))));

        var service = new BootConfigurationService(adapter);

        var response = service.SetDefaultEntry(new SetDefaultEntryRequest("entry-9"));

        Assert.False(response.Success);
        Assert.Equal("entry_not_found", response.ErrorCode);
        Assert.NotNull(response.State);
        Assert.Null(adapter.DefaultEntryId);
    }

    [Fact]
    public void SetTimeout_translates_the_mode_and_updates_the_adapter()
    {
        var adapter = new FakeBootConfigurationAdapter(
            new BootConfigurationSnapshot(
                CurrentDefaultEntryId: "entry-2",
                TimeoutSeconds: 30,
                Entries: ImmutableArray.Create(
                    new BootConfigurationEntry("entry-2", "Windows 11", true))));

        var service = new BootConfigurationService(adapter);

        var response = service.SetTimeout(new SetTimeoutRequest(BootMenuTimeoutMode.Off));

        Assert.True(response.Success);
        Assert.Null(response.ErrorCode);
        Assert.Null(response.ErrorMessage);
        Assert.NotNull(response.State);
        Assert.Equal(0, adapter.TimeoutSeconds);
        Assert.Equal(0, response.State!.TimeoutSeconds);
    }

    private sealed class FakeBootConfigurationAdapter : IBootConfigurationAdapter
    {
        private readonly BootConfigurationSnapshot _snapshot;

        public FakeBootConfigurationAdapter(BootConfigurationSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public string? DefaultEntryId { get; private set; }

        public int? TimeoutSeconds { get; private set; }

        public BootConfigurationSnapshot ReadState() => _snapshot;

        public void SetDefaultEntry(string entryId)
        {
            DefaultEntryId = entryId;
        }

        public void SetTimeout(int timeoutSeconds)
        {
            TimeoutSeconds = timeoutSeconds;
        }
    }
}
