namespace WindowsBootSwitcher.Tray;

/// <summary>
/// Decides whether opening the menu should refresh state, so quickly reopening the menu does not
/// send a request to the service each time.
/// </summary>
public sealed class MenuOpenRefreshPolicy(TimeProvider timeProvider, TimeSpan throttleWindow)
{
    public static readonly TimeSpan DefaultThrottleWindow = TimeSpan.FromSeconds(2);

    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private DateTimeOffset? _lastSuccessfulRefresh;

    public MenuOpenRefreshPolicy()
        : this(TimeProvider.System, DefaultThrottleWindow)
    {
    }

    public bool ShouldRefreshOnOpen() =>
        _lastSuccessfulRefresh is not { } last || _timeProvider.GetUtcNow() - last >= throttleWindow;

    public void RecordSuccessfulRefresh() => _lastSuccessfulRefresh = _timeProvider.GetUtcNow();
}
