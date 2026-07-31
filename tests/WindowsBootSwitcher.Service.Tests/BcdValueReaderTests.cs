using WindowsBootSwitcher.Service.Boot;
using Xunit;

namespace WindowsBootSwitcher.Service.Tests;

public sealed class BcdValueReaderTests
{
    [Fact]
    public void IsSuccess_treats_boolean_true_as_success()
    {
        // BCD WMI methods are declared "boolean Method(...)" where TRUE means success. Reading the
        // status numerically would invert the meaning and fail every real operation.
        Assert.True(BcdValueReader.IsSuccess(true, "SetIntegerElement"));
    }

    [Fact]
    public void IsSuccess_treats_boolean_false_as_failure()
    {
        Assert.False(BcdValueReader.IsSuccess(false, "GetElement"));
    }

    [Theory]
    [InlineData(1u, true)]
    [InlineData(0u, false)]
    public void IsSuccess_accepts_numeric_marshalling_of_the_boolean(uint value, bool expected)
    {
        Assert.Equal(expected, BcdValueReader.IsSuccess(value, "OpenStore"));
    }

    [Fact]
    public void IsSuccess_throws_when_the_status_is_absent()
    {
        var exception = Assert.Throws<BootConfigurationException>(
            () => BcdValueReader.IsSuccess(null, "OpenStore"));

        Assert.Equal("wmi_error", exception.ErrorCode);
    }

    [Fact]
    public void IsSuccess_throws_when_the_status_is_unreadable()
    {
        var exception = Assert.Throws<BootConfigurationException>(
            () => BcdValueReader.IsSuccess(new object(), "OpenStore"));

        Assert.Equal("wmi_error", exception.ErrorCode);
    }

    [Fact]
    public void ReadUInt64_parses_the_string_form_used_for_uint64_elements()
    {
        // COM Automation has no uint64, so the provider surfaces the value as a string.
        Assert.Equal(30UL, BcdValueReader.ReadUInt64("30"));
    }

    [Theory]
    [InlineData((ulong)42, 42UL)]
    [InlineData((uint)42, 42UL)]
    [InlineData(42, 42UL)]
    public void ReadUInt64_reads_numeric_forms(object value, ulong expected)
    {
        Assert.Equal(expected, BcdValueReader.ReadUInt64(value));
    }

    [Fact]
    public void ReadUInt64_returns_null_for_missing_or_unparsable_values()
    {
        Assert.Null(BcdValueReader.ReadUInt64(null));
        Assert.Null(BcdValueReader.ReadUInt64("not-a-number"));
        Assert.Null(BcdValueReader.ReadUInt64(new object()));
    }

    [Fact]
    public void ReadUInt32_rejects_values_outside_the_range()
    {
        Assert.Equal(0x10200003u, BcdValueReader.ReadUInt32(0x10200003u));
        Assert.Null(BcdValueReader.ReadUInt32(ulong.MaxValue));
    }

    [Fact]
    public void ClampToSeconds_saturates_instead_of_overflowing()
    {
        // A nonsensical store value must not throw OverflowException out of the read path.
        Assert.Equal(int.MaxValue, BcdValueReader.ClampToSeconds(ulong.MaxValue));
        Assert.Equal(30, BcdValueReader.ClampToSeconds(30));
    }
}
