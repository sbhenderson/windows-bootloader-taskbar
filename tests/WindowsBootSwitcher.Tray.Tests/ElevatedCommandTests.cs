using WindowsBootSwitcher.Contracts;
using WindowsBootSwitcher.Contracts.Responses;
using WindowsBootSwitcher.Tray.Elevation;
using Xunit;

namespace WindowsBootSwitcher.Tray.Tests;

public sealed class ElevatedCommandTests
{
    [Theory]
    [InlineData("{9dea862c-5cdd-4e70-acc1-f32b344d4795}")]
    [InlineData("{current}")]
    public void Set_default_round_trips_through_the_command_line(string entryId)
    {
        var arguments = ElevatedCommand.SetDefault(entryId).ToArguments();

        Assert.True(ElevatedCommand.TryParse(SplitQuoted(arguments), out var parsed));
        Assert.Equal(ElevatedCommand.SetDefault(entryId), parsed);
    }

    [Theory]
    [InlineData(BootMenuTimeoutMode.Off)]
    [InlineData(BootMenuTimeoutMode.ThirtySeconds)]
    public void Set_timeout_round_trips_through_the_command_line(BootMenuTimeoutMode mode)
    {
        var arguments = ElevatedCommand.SetTimeout(mode).ToArguments();

        Assert.True(ElevatedCommand.TryParse(SplitQuoted(arguments), out var parsed));
        Assert.Equal(ElevatedCommand.SetTimeout(mode), parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("quo\"te")]
    [InlineData("back\\slash")]
    [InlineData("tab\tid")]
    [InlineData("caf\u00e9")]
    public void Unsafe_arguments_are_rejected(string value)
    {
        Assert.False(ElevatedCommand.IsSafeArgument(value));
        Assert.False(ElevatedCommand.TryParse([ElevatedCommand.SetDefaultSwitch, value], out _));
    }

    [Fact]
    public void Overlong_arguments_are_rejected()
    {
        Assert.False(ElevatedCommand.IsSafeArgument(new string('a', 129)));
        Assert.True(ElevatedCommand.IsSafeArgument(new string('a', 128)));
    }

    [Theory]
    [InlineData("--elevated-set-timeout|Forever")]
    [InlineData("--elevated-set-timeout|7")]
    [InlineData("--elevated-set-default")]
    [InlineData("--elevated-set-default|{a}|extra")]
    [InlineData("--something-else|{a}")]
    public void Malformed_helper_arguments_do_not_parse(string joinedArgs)
    {
        Assert.False(ElevatedCommand.TryParse(joinedArgs.Split('|'), out _));
    }

    [Fact]
    public void Only_helper_switches_trigger_helper_mode()
    {
        Assert.True(ElevatedCommand.IsHelperInvocation([ElevatedCommand.SetTimeoutSwitch, "Off"]));
        Assert.False(ElevatedCommand.IsHelperInvocation([]));
        Assert.False(ElevatedCommand.IsHelperInvocation(["--other"]));
    }

    [Fact]
    public void Success_maps_to_exit_code_zero_and_back()
    {
        var exitCode = ElevatedCommand.ToExitCode(new BootOperationResponse(true, null, null, null));

        Assert.Equal(0, exitCode);
        Assert.True(ElevatedCommand.FromExitCode(exitCode).Success);
    }

    [Theory]
    [InlineData("access_denied")]
    [InlineData("entry_not_found")]
    [InlineData("wmi_error")]
    [InlineData("remote_client_rejected")]
    public void Known_error_codes_survive_the_exit_code(string errorCode)
    {
        var exitCode = ElevatedCommand.ToExitCode(new BootOperationResponse(false, errorCode, "detail", null));
        var response = ElevatedCommand.FromExitCode(exitCode);

        Assert.False(response.Success);
        Assert.Equal(errorCode, response.ErrorCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(999)]
    [InlineData(-1)]
    public void Other_exit_codes_map_to_elevation_failed(int exitCode)
    {
        var response = ElevatedCommand.FromExitCode(exitCode);

        Assert.False(response.Success);
        Assert.Equal(ElevatedCommand.ElevationFailedCode, response.ErrorCode);
    }

    [Fact]
    public void Unknown_service_error_maps_to_generic_failure()
    {
        Assert.Equal(1, ElevatedCommand.ToExitCode(new BootOperationResponse(false, "brand_new_code", null, null)));
    }

    /// <summary>Minimal splitter for the quoting ToArguments produces (no escapes are ever emitted).</summary>
    private static string[] SplitQuoted(string commandLine)
    {
        var space = commandLine.IndexOf(' ', StringComparison.Ordinal);
        return [commandLine[..space], commandLine[(space + 1)..].Trim('"')];
    }
}
