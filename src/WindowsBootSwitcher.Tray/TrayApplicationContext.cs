using System.Drawing;
using System.Windows.Forms;
using WindowsBootSwitcher.Contracts;
using WindowsBootSwitcher.Tray.Client;
using WindowsBootSwitcher.Tray.Notifications;

namespace WindowsBootSwitcher.Tray;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly BootSwitchClient _client;
    private readonly TrayNotificationService _notifications;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly System.Windows.Forms.Timer _startupTimer;
    private readonly System.Windows.Forms.Timer _retryTimer;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private TrayState _state = TrayState.Connecting();
    private TimeSpan _nextRetryDelay = TimeSpan.FromSeconds(1);
    private bool _hasShownUnavailableNotification;

    public TrayApplicationContext()
        : this(new NamedPipeBootSwitchClient())
    {
    }

    internal TrayApplicationContext(BootSwitchClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));

        _contextMenu = new ContextMenuStrip();
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Windows Boot Switcher",
            Visible = true,
            ContextMenuStrip = _contextMenu
        };

        _notifications = new TrayNotificationService(_notifyIcon);
        _startupTimer = new System.Windows.Forms.Timer { Interval = 1 };
        _startupTimer.Tick += StartupTimerOnTick;

        _retryTimer = new System.Windows.Forms.Timer { Enabled = false };
        _retryTimer.Tick += RetryTimerOnTick;

        RebuildMenu();
        _startupTimer.Start();
    }

    private void StartupTimerOnTick(object? sender, EventArgs e)
    {
        _startupTimer.Stop();
        QueueRefresh();
    }

    private void RetryTimerOnTick(object? sender, EventArgs e)
    {
        _retryTimer.Stop();
        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (_lifetimeCts.IsCancellationRequested)
        {
            return;
        }

        _ = RefreshStateAsync();
    }

    private async Task RefreshStateAsync()
    {
        if (!await _refreshGate.WaitAsync(0, _lifetimeCts.Token).ConfigureAwait(true))
        {
            return;
        }

        try
        {
            var response = await _client.GetStateAsync(_lifetimeCts.Token).ConfigureAwait(true);
            if (response.Success && response.State is not null)
            {
                var wasUnavailable = _state.ConnectionStatus != TrayConnectionStatus.Available;
                _state = TrayState.Available(response.State);
                _nextRetryDelay = TimeSpan.FromSeconds(1);
                _hasShownUnavailableNotification = false;
                StopRetryTimer();
                RebuildMenu();

                if (wasUnavailable)
                {
                    _notifications.ShowSuccess("Connected to Windows Boot Switcher.");
                }

                return;
            }

            if (!response.Success)
            {
                _notifications.ShowError(response.ErrorMessage ?? "Unable to read boot state.");
            }
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (IsTransportFailure(exception))
        {
            HandleUnavailable(exception.Message);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private static bool IsTransportFailure(Exception exception) =>
        exception is IOException or TimeoutException;

    private void HandleUnavailable(string message)
    {
        _state = TrayState.UnavailableRetrying(message, _state.BootState);
        RebuildMenu();
        if (!_hasShownUnavailableNotification)
        {
            _notifications.ShowStatus(message);
            _hasShownUnavailableNotification = true;
        }
        ScheduleRetry();
    }

    private void ScheduleRetry()
    {
        _retryTimer.Stop();
        _retryTimer.Interval = (int)Math.Max(1, _nextRetryDelay.TotalMilliseconds);
        _retryTimer.Start();
        _nextRetryDelay = TimeSpan.FromMilliseconds(Math.Min(_nextRetryDelay.TotalMilliseconds * 2, 10000));
    }

    private void StopRetryTimer()
    {
        _retryTimer.Stop();
        _nextRetryDelay = TimeSpan.FromSeconds(1);
    }

    private void RebuildMenu()
    {
        _contextMenu.Items.Clear();
        var model = TrayMenuBuilder.Build(_state);

        foreach (var item in model.Items)
        {
            _contextMenu.Items.Add(BuildToolStripItem(item));
        }
    }

    private ToolStripItem BuildToolStripItem(TrayMenuItem item)
    {
        if (item.IsSeparator)
        {
            return new ToolStripSeparator();
        }

        var menuItem = new ToolStripMenuItem(item.Text)
        {
            Enabled = item.IsEnabled,
            Checked = item.IsChecked,
            CheckOnClick = false,
            Tag = item
        };

        if (item.Children.Length > 0)
        {
            foreach (var child in item.Children)
            {
                menuItem.DropDownItems.Add(BuildToolStripItem(child));
            }
        }
        else if (item.CommandKind is not null)
        {
            menuItem.Click += MenuItemOnClick;
        }

        return menuItem;
    }

    private async void MenuItemOnClick(object? sender, EventArgs e)
    {
        if (sender is not ToolStripItem { Tag: TrayMenuItem trayItem } || trayItem.CommandKind is null)
        {
            return;
        }

        try
        {
            switch (trayItem.CommandKind)
            {
                case TrayMenuCommandKind.Refresh:
                    QueueRefresh();
                    break;
                case TrayMenuCommandKind.Exit:
                    ExitThread();
                    break;
                case TrayMenuCommandKind.SetDefaultEntry:
                    await SetDefaultEntryAsync(trayItem).ConfigureAwait(true);
                    break;
                case TrayMenuCommandKind.SetTimeout:
                    await SetTimeoutAsync(trayItem).ConfigureAwait(true);
                    break;
            }
        }
        catch (Exception exception) when (IsTransportFailure(exception))
        {
            HandleUnavailable(exception.Message);
        }
    }

    private async Task SetDefaultEntryAsync(TrayMenuItem item)
    {
        var entryId = item.CommandArgument ?? string.Empty;
        var response = await _client.SetDefaultEntryAsync(entryId, _lifetimeCts.Token).ConfigureAwait(true);
        if (!response.Success)
        {
            _notifications.ShowError(response.ErrorMessage ?? $"Unable to set {item.Text} as the default boot entry.");
            return;
        }

        _notifications.ShowSuccess($"Default boot entry set to {item.Text}.");
        await RefreshStateAsync().ConfigureAwait(true);
    }

    private async Task SetTimeoutAsync(TrayMenuItem item)
    {
        if (!TryParseTimeout(item.CommandArgument, out var mode))
        {
            _notifications.ShowError("The selected timeout option is invalid.");
            return;
        }

        var response = await _client.SetTimeoutAsync(mode, _lifetimeCts.Token).ConfigureAwait(true);
        if (!response.Success)
        {
            _notifications.ShowError(response.ErrorMessage ?? $"Unable to set the boot menu timeout to {item.Text}.");
            return;
        }

        _notifications.ShowSuccess($"Boot menu timeout set to {item.Text}.");
        await RefreshStateAsync().ConfigureAwait(true);
    }

    private static bool TryParseTimeout(string? value, out BootMenuTimeoutMode mode)
    {
        if (string.Equals(value, nameof(BootMenuTimeoutMode.Off), StringComparison.Ordinal))
        {
            mode = BootMenuTimeoutMode.Off;
            return true;
        }

        if (string.Equals(value, nameof(BootMenuTimeoutMode.ThirtySeconds), StringComparison.Ordinal))
        {
            mode = BootMenuTimeoutMode.ThirtySeconds;
            return true;
        }

        mode = default;
        return false;
    }

    protected override void ExitThreadCore()
    {
        _lifetimeCts.Cancel();
        _startupTimer.Stop();
        _retryTimer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
        _startupTimer.Dispose();
        _retryTimer.Dispose();
        _lifetimeCts.Dispose();
        _refreshGate.Dispose();
        base.ExitThreadCore();
    }
}
