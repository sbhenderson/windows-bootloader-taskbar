using System.Globalization;

namespace WindowsBootSwitcher.Service.Boot;

/// <summary>
/// Pure value conversions for the BCD WMI provider.
/// </summary>
/// <remarks>
/// BCD methods are declared in the MOF as returning <c>boolean</c>, where <c>TRUE</c> means the
/// operation succeeded. This is the inverse of the classic WMI convention where a
/// <c>ReturnValue</c> of zero means success, so the status must never be interpreted numerically.
/// BCD also surfaces <c>uint64</c> element values as strings because COM Automation has no native
/// 64 bit unsigned type.
/// </remarks>
public static class BcdValueReader
{
    /// <summary>
    /// Interprets a BCD <c>ReturnValue</c>. Returns <see langword="true"/> when the operation
    /// succeeded.
    /// </summary>
    public static bool IsSuccess(object? returnValue, string methodName)
    {
        switch (returnValue)
        {
            case null:
                throw new BootConfigurationException(
                    "wmi_error",
                    $"BCD operation '{methodName}' returned no status value.");
            case bool success:
                return success;
        }

        // Some marshallers surface the boolean as 0/1; a non-zero value still means success.
        var numeric = ReadUInt64(returnValue);
        if (numeric is null)
        {
            throw new BootConfigurationException(
                "wmi_error",
                $"BCD operation '{methodName}' returned an unreadable status value.");
        }

        return numeric.Value != 0;
    }

    /// <summary>
    /// Reads a BCD unsigned 64 bit value, which the provider may deliver as a string.
    /// </summary>
    public static ulong? ReadUInt64(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case ulong unsigned64:
                return unsigned64;
            case uint unsigned32:
                return unsigned32;
            case int signed32 when signed32 >= 0:
                return (ulong)signed32;
            case long signed64 when signed64 >= 0:
                return (ulong)signed64;
            case string text:
                return ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
        }

        try
        {
            return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a BCD unsigned 32 bit value such as <c>BcdObject.Type</c>.
    /// </summary>
    public static uint? ReadUInt32(object? value)
    {
        var parsed = ReadUInt64(value);
        return parsed is null || parsed.Value > uint.MaxValue ? null : (uint)parsed.Value;
    }

    /// <summary>
    /// Converts a BCD timeout element into seconds, saturating instead of overflowing so a
    /// nonsensical store value cannot throw.
    /// </summary>
    public static int ClampToSeconds(ulong value) =>
        value > int.MaxValue ? int.MaxValue : (int)value;
}
