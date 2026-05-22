using System.Collections.Immutable;
using System.Text.Json;
using WindowsBootSwitcher.Contracts;
using WindowsBootSwitcher.Contracts.Responses;
using WindowsBootSwitcher.Contracts.Serialization;
using Xunit;

namespace WindowsBootSwitcher.Contracts.Tests;

public sealed class ContractJsonRoundTripTests
{
    [Fact]
    public void BootState_round_trips_through_source_generated_json()
    {
        var state = new BootState(
            CurrentDefaultEntryId: "entry-1",
            TimeoutSeconds: 30,
            Entries: ImmutableArray.Create(
                new BootEntry("entry-1", "Windows Boot Manager", true),
                new BootEntry("entry-2", "Linux", false)));

        var json = JsonSerializer.Serialize(state, ContractsJsonContext.Default.BootState);
        var roundTripped = JsonSerializer.Deserialize(json, ContractsJsonContext.Default.BootState);

        Assert.NotNull(roundTripped);
        Assert.Equal(state.CurrentDefaultEntryId, roundTripped!.CurrentDefaultEntryId);
        Assert.Equal(state.TimeoutSeconds, roundTripped.TimeoutSeconds);
        Assert.Equal(state.Entries.Length, roundTripped.Entries.Length);

        for (var index = 0; index < state.Entries.Length; index++)
        {
            Assert.Equal(state.Entries[index].Id, roundTripped.Entries[index].Id);
            Assert.Equal(state.Entries[index].DisplayName, roundTripped.Entries[index].DisplayName);
            Assert.Equal(state.Entries[index].IsDefault, roundTripped.Entries[index].IsDefault);
        }
    }

    [Fact]
    public void BootOperationResponse_round_trips_through_source_generated_json()
    {
        var state = new BootState(
            CurrentDefaultEntryId: "entry-1",
            TimeoutSeconds: 30,
            Entries: ImmutableArray.Create(new BootEntry("entry-1", "Windows Boot Manager", true)));

        var response = new BootOperationResponse(
            Success: true,
            ErrorCode: null,
            ErrorMessage: null,
            State: state);

        var json = JsonSerializer.Serialize(response, ContractsJsonContext.Default.BootOperationResponse);
        var roundTripped = JsonSerializer.Deserialize(json, ContractsJsonContext.Default.BootOperationResponse);

        Assert.NotNull(roundTripped);
        Assert.True(roundTripped!.Success);
        Assert.Null(roundTripped.ErrorCode);
        Assert.Null(roundTripped.ErrorMessage);
        Assert.NotNull(roundTripped.State);
        Assert.Equal(state.CurrentDefaultEntryId, roundTripped.State!.CurrentDefaultEntryId);
        Assert.Equal(state.TimeoutSeconds, roundTripped.State.TimeoutSeconds);
    }
}
