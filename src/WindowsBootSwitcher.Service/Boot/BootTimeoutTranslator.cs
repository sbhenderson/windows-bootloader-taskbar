using WindowsBootSwitcher.Contracts;

namespace WindowsBootSwitcher.Service.Boot;

public sealed class BootTimeoutTranslator
{
    public int Translate(BootMenuTimeoutMode mode) =>
        mode switch
        {
            BootMenuTimeoutMode.Off => 0,
            BootMenuTimeoutMode.ThirtySeconds => 30,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported boot menu timeout mode.")
        };
}
