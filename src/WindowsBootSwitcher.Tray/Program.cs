using System.Windows.Forms;

namespace WindowsBootSwitcher.Tray;

internal static class Program
{
    /// <summary>
    /// Session scoped so each logged on user gets one tray icon, but the machine wide Run key
    /// plus a manual launch cannot produce duplicates competing over the same service.
    /// </summary>
    private const string SingleInstanceMutexName = @"Local\WindowsBootSwitcher.Tray";

    [STAThread]
    private static void Main()
    {
        using var singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
