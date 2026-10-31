using System.Windows.Forms;
using WindowsBootSwitcher.Tray.Client;
using WindowsBootSwitcher.Tray.Elevation;

namespace WindowsBootSwitcher.Tray;

internal static class Program
{
    /// <summary>
    /// Session scoped so each logged on user gets one tray icon, but the machine wide Run key
    /// plus a manual launch cannot produce duplicates competing over the same service.
    /// </summary>
    private const string SingleInstanceMutexName = @"Local\WindowsBootSwitcher.Tray";

    [STAThread]
    private static int Main(string[] args)
    {
        // The elevated copy performs one change and exits. It runs before the single-instance
        // check because the normal tray is necessarily still running.
        if (ElevatedCommand.IsHelperInvocation(args))
        {
            return ElevatedHelper.Run(args, new NamedPipeBootSwitchClient());
        }

        using var singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
        return 0;
    }
}
