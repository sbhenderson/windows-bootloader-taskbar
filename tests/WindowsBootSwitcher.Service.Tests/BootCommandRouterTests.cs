using System.Collections.Immutable;
using System.Text.Json;
using WindowsBootSwitcher.Contracts;
using WindowsBootSwitcher.Contracts.Requests;
using WindowsBootSwitcher.Service.Boot;
using WindowsBootSwitcher.Service.Ipc;
using WindowsBootSwitcher.Service.Security;
using Xunit;

namespace WindowsBootSwitcher.Service.Tests;

public sealed class BootCommandRouterTests
{
    [Fact]
    public void Route_rejects_unknown_commands()
    {
        var router = CreateRouter();
        using var request = JsonDocument.Parse("{}");

        var response = router.Route("delete_boot_entry", request.RootElement, new CallerIdentity(true, false));

        Assert.False(response.Success);
        Assert.Equal("unknown_command", response.ErrorCode);
    }

    [Fact]
    public void Route_allows_get_state_for_local_interactive_users()
    {
        var router = CreateRouter();
        using var request = JsonDocument.Parse("{}");

        var response = router.Route("get_state", request.RootElement, new CallerIdentity(true, false));

        Assert.True(response.Success);
        Assert.NotNull(response.State);
        Assert.Equal(30, response.State!.TimeoutSeconds);
    }

    [Fact]
    public void Route_rejects_get_state_for_non_interactive_non_admin_callers()
    {
        var router = CreateRouter();
        using var request = JsonDocument.Parse("{}");

        var response = router.Route("get_state", request.RootElement, new CallerIdentity(false, false));

        Assert.False(response.Success);
        Assert.Equal("access_denied", response.ErrorCode);
    }

    [Fact]
    public void Route_rejects_mutating_commands_for_non_admins()
    {
        var router = CreateRouter();
        var payload = JsonSerializer.SerializeToElement(new SetDefaultEntryRequest("entry-2"));

        var response = router.Route("set_default_entry", payload, new CallerIdentity(true, false));

        Assert.False(response.Success);
        Assert.Equal("access_denied", response.ErrorCode);
    }

    [Fact]
    public void Route_executes_authorized_mutating_commands()
    {
        var router = CreateRouter();
        var payload = JsonSerializer.SerializeToElement(new SetTimeoutRequest(BootMenuTimeoutMode.Off));

        var response = router.Route("set_timeout", payload, new CallerIdentity(true, true));

        Assert.True(response.Success);
        Assert.NotNull(response.State);
        Assert.Equal(0, response.State!.TimeoutSeconds);
    }

    [Fact]
    public void Route_maps_boot_configuration_failures_during_get_state_to_error_code()
    {
        var router = new BootCommandRouter(
            new BootConfigurationService(new ThrowingBootConfigurationAdapter()),
            new CallerAuthorizationPolicy());
        using var request = JsonDocument.Parse("{}");

        var response = router.Route("get_state", request.RootElement, new CallerIdentity(true, false));

        Assert.False(response.Success);
        Assert.Equal("wmi_error", response.ErrorCode);
        Assert.Null(response.State);
    }

    private static BootCommandRouter CreateRouter()
    {
        var adapter = new FakeBootConfigurationAdapter(
            new BootConfigurationSnapshot(
                CurrentDefaultEntryId: "entry-1",
                TimeoutSeconds: 30,
                Entries: ImmutableArray.Create(
                    new BootConfigurationEntry("entry-1", "Windows Boot Manager", true),
                    new BootConfigurationEntry("entry-2", "Windows 11", true))));

        var service = new BootConfigurationService(adapter);
        var policy = new CallerAuthorizationPolicy();

        return new BootCommandRouter(service, policy);
    }

    private sealed class ThrowingBootConfigurationAdapter : IBootConfigurationAdapter
    {
        public BootConfigurationSnapshot ReadState() =>
            throw new BootConfigurationException("wmi_error", "Failed to read boot configuration from WMI.");

        public void SetDefaultEntry(string entryId) => throw new NotSupportedException();

        public void SetTimeout(int timeoutSeconds) => throw new NotSupportedException();
    }

    private sealed class FakeBootConfigurationAdapter : IBootConfigurationAdapter
    {
        private BootConfigurationSnapshot _snapshot;

        public FakeBootConfigurationAdapter(BootConfigurationSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public BootConfigurationSnapshot ReadState() => _snapshot;

        public void SetDefaultEntry(string entryId)
        {
            _snapshot = _snapshot with { CurrentDefaultEntryId = entryId };
        }

        public void SetTimeout(int timeoutSeconds)
        {
            _snapshot = _snapshot with { TimeoutSeconds = timeoutSeconds };
        }
    }
}
