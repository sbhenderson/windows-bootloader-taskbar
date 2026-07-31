using WindowsBootSwitcher.Contracts.Responses;
using Xunit;

namespace WindowsBootSwitcher.Tray.Tests;

public sealed class TrayErrorFormatterTests
{
    [Fact]
    public void Format_includes_the_error_code_so_failures_stay_diagnosable()
    {
        var response = new BootOperationResponse(false, "wmi_error", "Could not write the store.", null);

        var message = TrayErrorFormatter.Format(response, "fallback");

        Assert.Contains("Could not write the store.", message, StringComparison.Ordinal);
        Assert.Contains("[wmi_error]", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_adds_an_actionable_hint_for_access_denied()
    {
        var response = new BootOperationResponse(false, "access_denied", "Not authorized.", null);

        var message = TrayErrorFormatter.Format(response, "fallback");

        Assert.Contains("Administrator rights", message, StringComparison.Ordinal);
        Assert.Contains("[access_denied]", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_falls_back_when_the_service_sends_no_message()
    {
        var response = new BootOperationResponse(false, null, null, null);

        var message = TrayErrorFormatter.Format(response, "Unable to read boot state.");

        Assert.Equal("Unable to read boot state.", message);
    }

    [Fact]
    public void Format_uses_the_fallback_text_but_keeps_a_known_code()
    {
        var response = new BootOperationResponse(false, "entry_not_found", null, null);

        var message = TrayErrorFormatter.Format(response, "Unable to set the default entry.");

        Assert.Contains("Unable to set the default entry.", message, StringComparison.Ordinal);
        Assert.Contains("[entry_not_found]", message, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeErrorCode_returns_null_for_unknown_codes()
    {
        Assert.Null(TrayErrorFormatter.DescribeErrorCode("something_new"));
        Assert.Null(TrayErrorFormatter.DescribeErrorCode(null));
    }

    [Fact]
    public void DescribeTransportFailure_tells_the_user_the_service_is_not_running()
    {
        var message = TrayErrorFormatter.DescribeTransportFailure(new IOException("pipe missing"));

        Assert.Equal(TrayErrorFormatter.ServiceUnavailableMessage, message);
    }

    [Fact]
    public void DescribeTransportFailure_preserves_timeout_detail()
    {
        var message = TrayErrorFormatter.DescribeTransportFailure(new TimeoutException("service did not respond"));

        Assert.Equal("service did not respond", message);
    }

    [Fact]
    public void Format_rejects_a_null_response()
    {
        Assert.Throws<ArgumentNullException>(() => TrayErrorFormatter.Format(null!, "fallback"));
    }
}
