using WindowsBootSwitcher.Contracts.Responses;

namespace WindowsBootSwitcher.Tray;

/// <summary>
/// Turns service error codes and transport failures into messages a user can act on.
/// </summary>
public static class TrayErrorFormatter
{
    public const string ServiceUnavailableMessage =
        "The Windows Boot Switcher service is not running. Start the 'WindowsBootSwitcher' service and try again.";

    /// <summary>
    /// Builds a notification message from a failed response, appending the service error code so
    /// failures remain diagnosable.
    /// </summary>
    public static string Format(BootOperationResponse response, string fallbackMessage)
    {
        ArgumentNullException.ThrowIfNull(response);

        var message = string.IsNullOrWhiteSpace(response.ErrorMessage)
            ? fallbackMessage
            : response.ErrorMessage!;

        var hint = DescribeErrorCode(response.ErrorCode);
        if (hint is not null)
        {
            message = $"{message} {hint}";
        }

        return string.IsNullOrWhiteSpace(response.ErrorCode)
            ? message
            : $"{message} [{response.ErrorCode}]";
    }

    public static string? DescribeErrorCode(string? errorCode) => errorCode switch
    {
        "access_denied" => "Administrator rights are required; run the tray application elevated.",
        "remote_client_rejected" => "Only local clients may use this service.",
        "entry_not_found" => "The boot entry no longer exists; refresh and try again.",
        "bcd_object_not_found" => "The boot configuration entry could not be located.",
        "wmi_error" => "Windows could not read or update the boot configuration.",
        "invalid_timeout" or "invalid_timeout_mode" => "The requested timeout is not supported.",
        "invalid_entry_id" => "The boot entry identifier was not valid.",
        "invalid_request" or "request_too_large" or "unknown_command" => "The service rejected the request.",
        "invalid_response" => "The service sent a response the tray could not understand.",
        "internal_error" => "Check the Windows Application event log for details.",
        _ => null
    };

    /// <summary>
    /// Describes a pipe level failure, distinguishing "service is not there" from "service is
    /// hung" so the status message is actionable.
    /// </summary>
    public static string DescribeTransportFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception is TimeoutException
            ? exception.Message
            : ServiceUnavailableMessage;
    }
}
