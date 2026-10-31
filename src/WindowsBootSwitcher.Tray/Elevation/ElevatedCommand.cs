using System.Collections.Immutable;
using WindowsBootSwitcher.Contracts;
using WindowsBootSwitcher.Contracts.Responses;

namespace WindowsBootSwitcher.Tray.Elevation;

/// <summary>
/// The command line contract between the tray and its elevated copy. The elevated copy performs one
/// mutation and reports the outcome through its exit code, because it has no channel back to the
/// tray other than the process handle.
/// </summary>
public sealed record ElevatedCommand(ElevatedCommandKind Kind, string Argument)
{
    public const string SetDefaultSwitch = "--elevated-set-default";
    public const string SetTimeoutSwitch = "--elevated-set-timeout";

    public const int SuccessExitCode = 0;
    public const int FailureExitCode = 1;
    public const int TransportFailureExitCode = 2;
    public const int InvalidArgumentsExitCode = 3;
    private const int KnownErrorBase = 10;

    public const string ElevationCancelledCode = "elevation_cancelled";
    public const string ElevationFailedCode = "elevation_failed";
    public const string InvalidEntryIdCode = "invalid_entry_id";

    internal const int MaxArgumentLength = 128;

    /// <summary>
    /// Service error codes that survive the trip through an exit code. Append only: the index is
    /// the wire format between two copies of the same build.
    /// </summary>
    internal static readonly ImmutableArray<string> KnownErrorCodes =
    [
        "access_denied",
        "entry_not_found",
        "bcd_object_not_found",
        "wmi_error",
        "invalid_timeout",
        "invalid_timeout_mode",
        "invalid_entry_id",
        "invalid_request",
        "request_too_large",
        "unknown_command",
        "invalid_response",
        "internal_error",
        "remote_client_rejected",
    ];

    public static ElevatedCommand SetDefault(string entryId) => new(ElevatedCommandKind.SetDefaultEntry, entryId);

    public static ElevatedCommand SetTimeout(BootMenuTimeoutMode mode) => new(ElevatedCommandKind.SetTimeout, mode.ToString());

    /// <summary>
    /// Entry ids are brace wrapped GUIDs or short aliases. Anything that could change how the
    /// command line is split is rejected rather than escaped.
    /// </summary>
    public static bool IsSafeArgument(string? value) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= MaxArgumentLength
        && value.All(c => c > ' ' && c < (char)0x7F && c is not '"' and not '\\');

    public string ToArguments() =>
        $"{(Kind == ElevatedCommandKind.SetDefaultEntry ? SetDefaultSwitch : SetTimeoutSwitch)} \"{Argument}\"";

    /// <summary>True when <paramref name="args"/> asks for helper mode.</summary>
    public static bool IsHelperInvocation(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Count > 0 && (args[0] == SetDefaultSwitch || args[0] == SetTimeoutSwitch);
    }

    public static bool TryParse(IReadOnlyList<string> args, out ElevatedCommand? command)
    {
        ArgumentNullException.ThrowIfNull(args);
        command = null;
        if (args.Count != 2 || !IsSafeArgument(args[1]))
        {
            return false;
        }

        if (args[0] == SetDefaultSwitch)
        {
            command = SetDefault(args[1]);
            return true;
        }

        if (args[0] == SetTimeoutSwitch && Enum.TryParse<BootMenuTimeoutMode>(args[1], ignoreCase: false, out var mode) && Enum.IsDefined(mode))
        {
            command = SetTimeout(mode);
            return true;
        }

        return false;
    }

    public static int ToExitCode(BootOperationResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.Success)
        {
            return SuccessExitCode;
        }

        var index = response.ErrorCode is null ? -1 : KnownErrorCodes.IndexOf(response.ErrorCode);
        return index >= 0 ? KnownErrorBase + index : FailureExitCode;
    }

    public static BootOperationResponse FromExitCode(int exitCode) => exitCode switch
    {
        SuccessExitCode => new BootOperationResponse(true, null, null, null),
        TransportFailureExitCode => new BootOperationResponse(false, ElevationFailedCode, TrayErrorFormatter.ServiceUnavailableMessage, null),
        InvalidArgumentsExitCode => new BootOperationResponse(false, ElevationFailedCode, "The elevated helper rejected its arguments.", null),
        >= KnownErrorBase when exitCode - KnownErrorBase < KnownErrorCodes.Length =>
            new BootOperationResponse(false, KnownErrorCodes[exitCode - KnownErrorBase], null, null),
        _ => new BootOperationResponse(false, ElevationFailedCode, $"The elevated helper failed (exit code {exitCode}).", null),
    };
}

public enum ElevatedCommandKind
{
    SetDefaultEntry,
    SetTimeout,
}
