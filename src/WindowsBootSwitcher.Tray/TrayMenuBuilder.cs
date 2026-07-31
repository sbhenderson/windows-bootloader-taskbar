using System.Collections.Immutable;
using System.Linq;
using WindowsBootSwitcher.Contracts;

namespace WindowsBootSwitcher.Tray;

public static class TrayMenuBuilder
{
    public static TrayMenuModel Build(TrayState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var items = ImmutableArray.CreateBuilder<TrayMenuItem>();

        if (state.ConnectionStatus != TrayConnectionStatus.Available)
        {
            items.Add(TrayMenuItem.Status(state.StatusMessage ?? "Service unavailable. Retrying..."));
        }

        if (state.BootState is not null)
        {
            items.Add(TrayMenuItem.Header($"Current default: {GetCurrentDefaultDisplayName(state.BootState)}"));

            foreach (var entry in state.BootState.Entries)
            {
                items.Add(TrayMenuItem.Action(
                    text: entry.DisplayName,
                    commandKind: TrayMenuCommandKind.SetDefaultEntry,
                    commandArgument: entry.Id,
                    isEnabled: state.IsAvailable,
                    isChecked: entry.IsDefault));
            }

            items.Add(BuildTimeoutSubmenu(state.BootState.TimeoutSeconds, state.IsAvailable));
            items.Add(TrayMenuItem.Separator());
        }
        else if (state.ConnectionStatus != TrayConnectionStatus.Available)
        {
            items.Add(TrayMenuItem.Separator());
        }

        items.Add(TrayMenuItem.Action("Refresh", TrayMenuCommandKind.Refresh, isEnabled: true));
        items.Add(TrayMenuItem.Action("Exit", TrayMenuCommandKind.Exit, isEnabled: true));

        return new TrayMenuModel(items.ToImmutable());
    }

    private static TrayMenuItem BuildTimeoutSubmenu(int timeoutSeconds, bool isEnabled)
    {
        var children = ImmutableArray.Create(
            TrayMenuItem.Action(
                text: "Off",
                commandKind: TrayMenuCommandKind.SetTimeout,
                commandArgument: nameof(BootMenuTimeoutMode.Off),
                isEnabled: isEnabled,
                isChecked: timeoutSeconds == 0),
            TrayMenuItem.Action(
                text: "30 seconds",
                commandKind: TrayMenuCommandKind.SetTimeout,
                commandArgument: nameof(BootMenuTimeoutMode.ThirtySeconds),
                isEnabled: isEnabled,
                isChecked: timeoutSeconds == 30));

        return TrayMenuItem.Submenu("Timeout", children, isEnabled);
    }

    private static string GetCurrentDefaultDisplayName(BootState bootState)
    {
        // CurrentDefaultEntryId is the authoritative field; the per-entry flag is only a fallback.
        var byId = bootState.Entries.FirstOrDefault(entry => string.Equals(entry.Id, bootState.CurrentDefaultEntryId, StringComparison.OrdinalIgnoreCase));
        if (byId is not null)
        {
            return byId.DisplayName;
        }

        return bootState.Entries.FirstOrDefault(entry => entry.IsDefault)?.DisplayName ?? "Unknown";
    }
}

public sealed record TrayMenuModel(ImmutableArray<TrayMenuItem> Items);

public enum TrayMenuCommandKind
{
    SetDefaultEntry,
    SetTimeout,
    Refresh,
    Exit
}

public sealed record TrayMenuItem
{
    private TrayMenuItem(
        string text,
        bool isEnabled,
        bool isChecked,
        TrayMenuCommandKind? commandKind,
        string? commandArgument,
        ImmutableArray<TrayMenuItem> children,
        bool isSeparator)
    {
        Text = text;
        IsEnabled = isEnabled;
        IsChecked = isChecked;
        CommandKind = commandKind;
        CommandArgument = commandArgument;
        Children = children;
        IsSeparator = isSeparator;
    }

    public string Text { get; }

    public bool IsEnabled { get; }

    public bool IsDisabled => !IsEnabled;

    public bool IsChecked { get; }

    public TrayMenuCommandKind? CommandKind { get; }

    public string? CommandArgument { get; }

    public ImmutableArray<TrayMenuItem> Children { get; }

    public bool IsSeparator { get; }

    public static TrayMenuItem Action(string text, TrayMenuCommandKind commandKind, string? commandArgument = null, bool isEnabled = true, bool isChecked = false)
        => new(text, isEnabled, isChecked, commandKind, commandArgument, [], false);

    public static TrayMenuItem Header(string text)
        => new(text, false, false, null, null, [], false);

    public static TrayMenuItem Status(string text)
        => new(text, false, false, null, null, [], false);

    public static TrayMenuItem Submenu(string text, ImmutableArray<TrayMenuItem> children, bool isEnabled = true)
        => new(text, isEnabled, false, null, null, children, false);

    public static TrayMenuItem Separator()
        => new(string.Empty, false, false, null, null, [], true);
}
