using Xunit;

namespace WindowsBootSwitcher.Tray.Tests;

public sealed class MenuOpenRefreshPolicyTests
{
    private sealed class ManualTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Refreshes_on_first_open()
    {
        var policy = new MenuOpenRefreshPolicy(new ManualTimeProvider(), TimeSpan.FromSeconds(2));

        Assert.True(policy.ShouldRefreshOnOpen());
    }

    [Fact]
    public void Skips_refresh_inside_the_throttle_window()
    {
        var time = new ManualTimeProvider();
        var policy = new MenuOpenRefreshPolicy(time, TimeSpan.FromSeconds(2));
        policy.RecordSuccessfulRefresh();

        time.Now += TimeSpan.FromMilliseconds(1999);

        Assert.False(policy.ShouldRefreshOnOpen());
    }

    [Fact]
    public void Refreshes_again_once_the_window_has_passed()
    {
        var time = new ManualTimeProvider();
        var policy = new MenuOpenRefreshPolicy(time, TimeSpan.FromSeconds(2));
        policy.RecordSuccessfulRefresh();

        time.Now += TimeSpan.FromSeconds(2);

        Assert.True(policy.ShouldRefreshOnOpen());
    }

    [Fact]
    public void Failed_refreshes_do_not_start_the_throttle()
    {
        // Only RecordSuccessfulRefresh starts the window, so an outage never suppresses a retry on open.
        var policy = new MenuOpenRefreshPolicy(new ManualTimeProvider(), TimeSpan.FromSeconds(2));

        Assert.True(policy.ShouldRefreshOnOpen());
        Assert.True(policy.ShouldRefreshOnOpen());
    }
}
