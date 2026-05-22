using System.Collections.Immutable;
using WindowsBootSwitcher.Contracts;
using WindowsBootSwitcher.Contracts.Requests;
using WindowsBootSwitcher.Contracts.Responses;

namespace WindowsBootSwitcher.Service.Boot;

public sealed class BootConfigurationService
{
    private readonly IBootConfigurationAdapter _adapter;
    private readonly BootEntryFilter _entryFilter;
    private readonly BootTimeoutTranslator _timeoutTranslator;

    public BootConfigurationService(
        IBootConfigurationAdapter adapter,
        BootEntryFilter? entryFilter = null,
        BootTimeoutTranslator? timeoutTranslator = null)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _entryFilter = entryFilter ?? new BootEntryFilter();
        _timeoutTranslator = timeoutTranslator ?? new BootTimeoutTranslator();
    }

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
        var timeoutSeconds = _timeoutTranslator.Translate(request.Mode);

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

        return new BootState(
            CurrentDefaultEntryId: visibleDefaultEntryId,
            TimeoutSeconds: timeoutSeconds,
            Entries: entries);
    }
}
