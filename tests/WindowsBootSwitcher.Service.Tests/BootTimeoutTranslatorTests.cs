using WindowsBootSwitcher.Contracts;
using WindowsBootSwitcher.Service.Boot;
using Xunit;

namespace WindowsBootSwitcher.Service.Tests;

public sealed class BootTimeoutTranslatorTests
{
    [Theory]
    [InlineData(BootMenuTimeoutMode.Off, 0)]
    [InlineData(BootMenuTimeoutMode.ThirtySeconds, 30)]
    public void Translate_maps_supported_modes_to_seconds(BootMenuTimeoutMode mode, int expectedSeconds)
    {
        var translator = new BootTimeoutTranslator();

        var seconds = translator.Translate(mode);

        Assert.Equal(expectedSeconds, seconds);
    }

    [Fact]
    public void Translate_throws_for_unsupported_values()
    {
        var translator = new BootTimeoutTranslator();

        Assert.Throws<ArgumentOutOfRangeException>(() => translator.Translate((BootMenuTimeoutMode)99));
    }
}
