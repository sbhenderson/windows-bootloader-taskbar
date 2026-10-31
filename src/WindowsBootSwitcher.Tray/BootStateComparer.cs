using WindowsBootSwitcher.Contracts;

namespace WindowsBootSwitcher.Tray;

/// <summary>
/// Value comparison for <see cref="BootState"/>. Record equality is not enough because
/// <c>ImmutableArray</c> compares by reference, so two reads of identical state never compare equal.
/// </summary>
public static class BootStateComparer
{
    public static bool AreEquivalent(BootState? left, BootState? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return string.Equals(left.CurrentDefaultEntryId, right.CurrentDefaultEntryId, StringComparison.OrdinalIgnoreCase)
            && left.TimeoutSeconds == right.TimeoutSeconds
            && left.Entries.SequenceEqual(right.Entries);
    }
}
