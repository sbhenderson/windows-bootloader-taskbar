using WindowsBootSwitcher.Contracts;
using WindowsBootSwitcher.Contracts.Responses;
using WindowsBootSwitcher.Tray.Client;
using WindowsBootSwitcher.Tray.Elevation;
using Xunit;

namespace WindowsBootSwitcher.Tray.Tests;

public sealed class ElevatingBootSwitchClientTests
{
    private static readonly BootOperationResponse Ok = new(true, null, null, null);

    [Fact]
    public async Task Reads_never_elevate()
    {
        var inner = new RecordingClient();
        var launcher = new FakeLauncher(isElevated: false);
        var client = new ElevatingBootSwitchClient(inner, launcher);

        await client.GetStateAsync(CancellationToken.None);

        Assert.Equal(["get_state"], inner.Calls);
        Assert.Empty(launcher.Launches);
    }

    [Fact]
    public async Task Non_elevated_set_default_launches_the_helper()
    {
        var inner = new RecordingClient();
        var launcher = new FakeLauncher(isElevated: false) { ExitCode = 0 };
        var client = new ElevatingBootSwitchClient(inner, launcher);

        var response = await client.SetDefaultEntryAsync("{abc}", CancellationToken.None);

        Assert.True(response.Success);
        Assert.Empty(inner.Calls);
        Assert.Equal(["--elevated-set-default \"{abc}\""], launcher.Launches);
    }

    [Fact]
    public async Task Non_elevated_set_timeout_launches_the_helper()
    {
        var launcher = new FakeLauncher(isElevated: false) { ExitCode = 0 };
        var client = new ElevatingBootSwitchClient(new RecordingClient(), launcher);

        await client.SetTimeoutAsync(BootMenuTimeoutMode.ThirtySeconds, CancellationToken.None);

        Assert.Equal(["--elevated-set-timeout \"ThirtySeconds\""], launcher.Launches);
    }

    [Fact]
    public async Task Elevated_tray_calls_the_service_directly()
    {
        var inner = new RecordingClient();
        var launcher = new FakeLauncher(isElevated: true);
        var client = new ElevatingBootSwitchClient(inner, launcher);

        await client.SetDefaultEntryAsync("{abc}", CancellationToken.None);
        await client.SetTimeoutAsync(BootMenuTimeoutMode.Off, CancellationToken.None);

        Assert.Equal(["set_default_entry", "set_timeout"], inner.Calls);
        Assert.Empty(launcher.Launches);
    }

    [Fact]
    public async Task Declined_prompt_is_reported_as_cancelled()
    {
        var launcher = new FakeLauncher(isElevated: false) { Cancel = true };
        var client = new ElevatingBootSwitchClient(new RecordingClient(), launcher);

        var response = await client.SetTimeoutAsync(BootMenuTimeoutMode.Off, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(ElevatedCommand.ElevationCancelledCode, response.ErrorCode);
    }

    [Fact]
    public async Task Helper_failure_code_is_surfaced()
    {
        var notFound = ElevatedCommand.ToExitCode(new BootOperationResponse(false, "entry_not_found", null, null));
        var launcher = new FakeLauncher(isElevated: false) { ExitCode = notFound };
        var client = new ElevatingBootSwitchClient(new RecordingClient(), launcher);

        var response = await client.SetDefaultEntryAsync("{abc}", CancellationToken.None);

        Assert.Equal("entry_not_found", response.ErrorCode);
    }

    [Fact]
    public async Task Unsafe_entry_id_never_starts_a_process()
    {
        var launcher = new FakeLauncher(isElevated: false);
        var client = new ElevatingBootSwitchClient(new RecordingClient(), launcher);

        var response = await client.SetDefaultEntryAsync("x\" --evil", CancellationToken.None);

        Assert.Equal(ElevatedCommand.InvalidEntryIdCode, response.ErrorCode);
        Assert.Empty(launcher.Launches);
    }

    [Fact]
    public void Helper_runs_the_parsed_command_and_returns_its_exit_code()
    {
        var inner = new RecordingClient { Response = new BootOperationResponse(false, "access_denied", null, null) };

        var exitCode = ElevatedHelper.Run([ElevatedCommand.SetDefaultSwitch, "{abc}"], inner);

        Assert.Equal(["set_default_entry"], inner.Calls);
        Assert.Equal("access_denied", ElevatedCommand.FromExitCode(exitCode).ErrorCode);
    }

    [Fact]
    public void Helper_rejects_bad_arguments_without_calling_the_service()
    {
        var inner = new RecordingClient();

        var exitCode = ElevatedHelper.Run([ElevatedCommand.SetTimeoutSwitch, "Forever"], inner);

        Assert.Equal(ElevatedCommand.InvalidArgumentsExitCode, exitCode);
        Assert.Empty(inner.Calls);
    }

    [Fact]
    public void Helper_reports_transport_failure()
    {
        var inner = new RecordingClient { Throw = new IOException("pipe gone") };

        Assert.Equal(ElevatedCommand.TransportFailureExitCode, ElevatedHelper.Run([ElevatedCommand.SetTimeoutSwitch, "Off"], inner));
    }

    private sealed class RecordingClient : IBootSwitchClient
    {
        public List<string> Calls { get; } = [];

        public BootOperationResponse Response { get; init; } = Ok;

        public Exception? Throw { get; init; }

        public Task<BootOperationResponse> GetStateAsync(CancellationToken cancellationToken) => Record("get_state");

        public Task<BootOperationResponse> SetDefaultEntryAsync(string entryId, CancellationToken cancellationToken) => Record("set_default_entry");

        public Task<BootOperationResponse> SetTimeoutAsync(BootMenuTimeoutMode mode, CancellationToken cancellationToken) => Record("set_timeout");

        private Task<BootOperationResponse> Record(string call)
        {
            Calls.Add(call);
            return Throw is null ? Task.FromResult(Response) : Task.FromException<BootOperationResponse>(Throw);
        }
    }

    private sealed class FakeLauncher(bool isElevated) : IElevatedProcessLauncher
    {
        public bool IsCurrentProcessElevated { get; } = isElevated;

        public List<string> Launches { get; } = [];

        public int ExitCode { get; init; }

        public bool Cancel { get; init; }

        public Task<int> RunElevatedAsync(string arguments, CancellationToken cancellationToken)
        {
            Launches.Add(arguments);
            return Cancel ? Task.FromException<int>(new ElevationCancelledException()) : Task.FromResult(ExitCode);
        }
    }
}
