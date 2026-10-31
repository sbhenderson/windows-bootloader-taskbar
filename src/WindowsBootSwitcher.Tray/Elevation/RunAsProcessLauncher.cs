using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace WindowsBootSwitcher.Tray.Elevation;

/// <summary>Starts the tray executable through ShellExecute's <c>runas</c> verb (a UAC prompt).</summary>
public sealed class RunAsProcessLauncher : IElevatedProcessLauncher
{
    /// <summary>ERROR_CANCELLED: the user chose "No" on the consent prompt.</summary>
    private const int ErrorCancelled = 1223;

    public bool IsCurrentProcessElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            // IsInRole only matches an enabled group, so a UAC-filtered admin token returns false.
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public async Task<int> RunElevatedAsync(string arguments, CancellationToken cancellationToken)
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to determine the tray executable path.");

        var startInfo = new ProcessStartInfo(executable, arguments)
        {
            UseShellExecute = true,
            Verb = "runas",
        };

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            throw new ElevationCancelledException("The administrator prompt was declined.", exception);
        }

        using (process ?? throw new InvalidOperationException("The elevated helper did not start."))
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode;
        }
    }
}
