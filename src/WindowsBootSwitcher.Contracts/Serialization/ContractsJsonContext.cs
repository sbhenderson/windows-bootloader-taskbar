using System.Text.Json.Serialization;
using WindowsBootSwitcher.Contracts.Requests;
using WindowsBootSwitcher.Contracts.Responses;

namespace WindowsBootSwitcher.Contracts.Serialization;

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(BootEntry))]
[JsonSerializable(typeof(BootState))]
[JsonSerializable(typeof(BootMenuTimeoutMode))]
[JsonSerializable(typeof(GetStateRequest))]
[JsonSerializable(typeof(SetDefaultEntryRequest))]
[JsonSerializable(typeof(SetTimeoutRequest))]
[JsonSerializable(typeof(BootOperationResponse))]
public partial class ContractsJsonContext : JsonSerializerContext;
