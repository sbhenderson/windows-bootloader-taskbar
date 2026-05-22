using System.Windows.Forms;

namespace WindowsBootSwitcher.Tray.Notifications;

public sealed class TrayNotificationService
{
    private const int BalloonDurationMilliseconds = 3000;
    private const string Title = "Windows Boot Switcher";
    private readonly NotifyIcon _notifyIcon;

    public TrayNotificationService(NotifyIcon notifyIcon)
    {
        _notifyIcon = notifyIcon ?? throw new ArgumentNullException(nameof(notifyIcon));
    }

    public void ShowSuccess(string message) => Show(message, ToolTipIcon.Info);

    public void ShowError(string message) => Show(message, ToolTipIcon.Error);

    public void ShowStatus(string message) => Show(message, ToolTipIcon.Info);

    private void Show(string message, ToolTipIcon icon)
    {
        _notifyIcon.BalloonTipTitle = Title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(BalloonDurationMilliseconds);
    }
}
