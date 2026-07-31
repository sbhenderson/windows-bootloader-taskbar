using System.Collections.Immutable;
using System.Text.Json;
using WindowsBootSwitcher.Service.Boot;
using WindowsBootSwitcher.Service.Diagnostics;
using WindowsBootSwitcher.Service.Ipc;
using WindowsBootSwitcher.Service.Security;
using Xunit;

namespace WindowsBootSwitcher.Service.Tests;

public sealed class BootCommandRouterValidationTests
{
    [Fact]
    public void Route_rejects_entry_ids_longer_than_the_bound()
    {
        var router = CreateRouter();
        var oversizedId = new string('x', BootCommandRouter.MaxEntryIdLength + 1);
        using var request = JsonDocument.Parse(JsonSerializer.Serialize(new { EntryId = oversizedId }));

        var response = router.Route("set_default_entry", request.RootElement, new CallerIdentity(true, true));

        Assert.False(response.Success);
        Assert.Equal("invalid_request", response.ErrorCode);
    }

    [Fact]
    public void Route_accepts_entry_ids_at_the_bound()
    {
        var router = CreateRouter();
        var boundaryId = new string('x', BootCommandRouter.MaxEntryIdLength);
        using var request = JsonDocument.Parse(JsonSerializer.Serialize(new { EntryId = boundaryId }));

        var response = router.Route("set_default_entry", request.RootElement, new CallerIdentity(true, true));

        // Not a validation failure: it gets as far as the entry lookup.
        Assert.Equal("entry_not_found", response.ErrorCode);
    }

    [Fact]
    public void Route_rejects_blank_entry_ids()
    {
        var router = CreateRouter();
        using var request = JsonDocument.Parse(JsonSerializer.Serialize(new { EntryId = "   " }));

        var response = router.Route("set_default_entry", request.RootElement, new CallerIdentity(true, true));

        Assert.False(response.Success);
        Assert.Equal("invalid_request", response.ErrorCode);
    }

    [Theory]
    [InlineData("GET_STATE")]
    [InlineData("  get_state  ")]
    public void Route_normalizes_command_casing_and_whitespace(string command)
    {
        var router = CreateRouter();
        using var request = JsonDocument.Parse("{}");

        var response = router.Route(command, request.RootElement, new CallerIdentity(true, false));

        Assert.True(response.Success);
    }

    private static BootCommandRouter CreateRouter() =>
        new(new BootConfigurationService(new StubAdapter()), new CallerAuthorizationPolicy());

    private sealed class StubAdapter : IBootConfigurationAdapter
    {
        public BootConfigurationSnapshot ReadState() => new(
            CurrentDefaultEntryId: "entry-1",
            TimeoutSeconds: 30,
            Entries: ImmutableArray.Create(new BootConfigurationEntry("entry-1", "Windows 11", true)));

        public void SetDefaultEntry(string entryId)
        {
        }

        public void SetTimeout(int timeoutSeconds)
        {
        }
    }
}

public sealed class EventLogWriterSanitizeTests
{
    [Fact]
    public void Sanitize_truncates_untrusted_text()
    {
        var sanitized = EventLogWriter.Sanitize(new string('a', 500), maxLength: 32);

        Assert.Equal(new string('a', 32) + "...", sanitized);
    }

    [Fact]
    public void Sanitize_replaces_control_characters_that_could_forge_log_lines()
    {
        var sanitized = EventLogWriter.Sanitize("get\r\nstate\tnow");

        Assert.Equal("get??state?now", sanitized);
    }

    [Fact]
    public void Sanitize_handles_missing_values()
    {
        Assert.Equal(string.Empty, EventLogWriter.Sanitize(null));
        Assert.Equal(string.Empty, EventLogWriter.Sanitize(string.Empty));
    }
}
