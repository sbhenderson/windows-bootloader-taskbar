using System.Collections.Immutable;
using WindowsBootSwitcher.Contracts;
using Xunit;

namespace WindowsBootSwitcher.Tray.Tests;

public sealed class BootStateComparerTests
{
    private static BootState State(string? defaultId = "{a}", int timeout = 30, params BootEntry[] entries) =>
        new(defaultId, timeout, entries.Length == 0
            ? [new BootEntry("{a}", "Windows 11", true), new BootEntry("{b}", "Windows 10", false)]
            : ImmutableArray.Create(entries));

    [Fact]
    public void Separately_built_identical_states_are_equivalent()
    {
        Assert.True(BootStateComparer.AreEquivalent(State(), State()));
    }

    [Fact]
    public void Default_id_comparison_ignores_case()
    {
        Assert.True(BootStateComparer.AreEquivalent(State("{A}"), State("{a}")));
    }

    [Fact]
    public void Different_default_is_not_equivalent()
    {
        Assert.False(BootStateComparer.AreEquivalent(State("{a}"), State("{b}")));
    }

    [Fact]
    public void Different_timeout_is_not_equivalent()
    {
        Assert.False(BootStateComparer.AreEquivalent(State(timeout: 30), State(timeout: 0)));
    }

    [Fact]
    public void Renamed_entry_is_not_equivalent()
    {
        var renamed = State("{a}", 30, new BootEntry("{a}", "Windows 11 Pro", true), new BootEntry("{b}", "Windows 10", false));

        Assert.False(BootStateComparer.AreEquivalent(State(), renamed));
    }

    [Fact]
    public void Reordered_entries_are_not_equivalent()
    {
        var reordered = State("{a}", 30, new BootEntry("{b}", "Windows 10", false), new BootEntry("{a}", "Windows 11", true));

        Assert.False(BootStateComparer.AreEquivalent(State(), reordered));
    }

    [Fact]
    public void Nulls_compare_safely()
    {
        Assert.True(BootStateComparer.AreEquivalent(null, null));
        Assert.False(BootStateComparer.AreEquivalent(State(), null));
        Assert.False(BootStateComparer.AreEquivalent(null, State()));
    }
}
