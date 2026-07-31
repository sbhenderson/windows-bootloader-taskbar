using System.Collections.Immutable;
using WindowsBootSwitcher.Contracts;
using WindowsBootSwitcher.Contracts.Requests;
using WindowsBootSwitcher.Contracts.Responses;

namespace WindowsBootSwitcher.Service.Boot;

public sealed class BootConfigurationService(
    IBootConfigurationAdapter adapter,
    BootEntryFilter? entryFilter = null,
    BootTimeoutTranslator? timeoutTranslator = null)
{
    private readonly IBootConfigurationAdapter _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    private readonly BootEntryFilter _entryFilter = entryFilter ?? new BootEntryFilter();
    private readonly BootTimeoutTranslator _timeoutTranslator = timeoutTranslator ?? new BootTimeoutTranslator();

    public BootState GetState()
    {
        var snapshot = _adapter.ReadState();
        return BuildState(snapshot);
    }

    public BootOperationResponse SetDefaultEntry(SetDefaultEntryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var snapshot = _adapter.ReadState();
        var state = BuildState(snapshot);

        if (!state.Entries.Any(entry => string.Equals(entry.Id, request.EntryId, StringComparison.OrdinalIgnoreCase)))
        {
            return new BootOperationResponse(
                Success: false,
                ErrorCode: "entry_not_found",
                ErrorMessage: $"Boot entry '{request.EntryId}' was not found.",
                State: state);
        }

        try
        {
            _adapter.SetDefaultEntry(request.EntryId);
        }
        catch (BootConfigurationException exception)
        {
            return new BootOperationResponse(false, exception.ErrorCode, exception.Message, state);
        }

        return new BootOperationResponse(true, null, null, BuildState(snapshot, request.EntryId, null));
    }

    public BootOperationResponse SetTimeout(SetTimeoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var snapshot = _adapter.ReadState();
        int timeoutSeconds;
        try
        {
            timeoutSeconds = _timeoutTranslator.Translate(request.Mode);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return new BootOperationResponse(
                Success: false,
                ErrorCode: "invalid_timeout_mode",
                ErrorMessage: exception.Message,
                State: BuildState(snapshot));
        }

        try
        {
            _adapter.SetTimeout(timeoutSeconds);
        }
        catch (BootConfigurationException exception)
        {
            return new BootOperationResponse(false, exception.ErrorCode, exception.Message, BuildState(snapshot));
        }

        return new BootOperationResponse(true, null, null, BuildState(snapshot, null, timeoutSeconds));
    }

    private BootState BuildState(BootConfigurationSnapshot snapshot, string? currentDefaultEntryIdOverride = null, int? timeoutSecondsOverride = null)
    {
        var entries = _entryFilter.Filter(snapshot);
        var currentDefaultEntryId = currentDefaultEntryIdOverride ?? snapshot.CurrentDefaultEntryId;
        var timeoutSeconds = timeoutSecondsOverride ?? snapshot.TimeoutSeconds;
        var visibleDefaultEntryId = entries.Any(entry => string.Equals(entry.Id, currentDefaultEntryId, StringComparison.OrdinalIgnoreCase))
            ? currentDefaultEntryId
            : null;

        // The filter derives IsDefault from the snapshot, which is stale once a mutation
        // supplies an override, so the flag is always recomputed from the effective default.
        var resolvedEntries = ImmutableArray.CreateRange(
            entries,
            entry => entry with { IsDefault = string.Equals(entry.Id, visibleDefaultEntryId, StringComparison.OrdinalIgnoreCase) });

        return new BootState(
            CurrentDefaultEntryId: visibleDefaultEntryId,
            TimeoutSeconds: timeoutSeconds,
            Entries: resolvedEntries);
    }
}
