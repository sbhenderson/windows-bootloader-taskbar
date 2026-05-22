using System.Collections.Immutable;

namespace WindowsBootSwitcher.Contracts;

public sealed record BootState(string? CurrentDefaultEntryId, int TimeoutSeconds, ImmutableArray<BootEntry> Entries);
