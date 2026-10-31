namespace WindowsBootSwitcher.Tray.Elevation;

public interface IElevatedProcessLauncher
{
    /// <summary>True when this process already holds an elevated administrator token.</summary>
    bool IsCurrentProcessElevated { get; }

    /// <summary>
    /// Runs the tray executable elevated with <paramref name="arguments"/> and returns its exit code.
    /// </summary>
    /// <exception cref="ElevationCancelledException">The user declined the UAC prompt.</exception>
    Task<int> RunElevatedAsync(string arguments, CancellationToken cancellationToken);
}

public sealed class ElevationCancelledException : Exception
{
    public ElevationCancelledException()
        : base("The administrator prompt was declined.")
    {
    }

    public ElevationCancelledException(string message)
        : base(message)
    {
    }

    public ElevationCancelledException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
