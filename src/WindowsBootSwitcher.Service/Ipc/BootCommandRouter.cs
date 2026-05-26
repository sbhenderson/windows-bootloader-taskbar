using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using WindowsBootSwitcher.Contracts.Requests;
using WindowsBootSwitcher.Contracts.Responses;
using WindowsBootSwitcher.Contracts.Serialization;
using WindowsBootSwitcher.Service.Boot;
using WindowsBootSwitcher.Service.Security;

namespace WindowsBootSwitcher.Service.Ipc;

public sealed class BootCommandRouter(BootConfigurationService bootConfigurationService, CallerAuthorizationPolicy authorizationPolicy)
{
    private readonly BootConfigurationService _bootConfigurationService = bootConfigurationService ?? throw new ArgumentNullException(nameof(bootConfigurationService));
    private readonly CallerAuthorizationPolicy _authorizationPolicy = authorizationPolicy ?? throw new ArgumentNullException(nameof(authorizationPolicy));

    public BootOperationResponse Route(string commandName, JsonElement payload, CallerIdentity caller)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return InvalidRequest("The command name was missing.");
        }

        return commandName.Trim().ToLowerInvariant() switch
        {
            "get_state" => RouteGetState(caller),
            "set_default_entry" => RouteSetDefaultEntry(payload, caller),
            "set_timeout" => RouteSetTimeout(payload, caller),
            _ => new BootOperationResponse(false, "unknown_command", $"Unknown command '{commandName}'.", null)
        };
    }

    private BootOperationResponse RouteGetState(CallerIdentity caller)
    {
        if (!_authorizationPolicy.CanRead(caller))
        {
            return new BootOperationResponse(false, "access_denied", "The caller is not authorized to read boot state.", null);
        }

        return new BootOperationResponse(true, null, null, _bootConfigurationService.GetState());
    }

    private BootOperationResponse RouteSetDefaultEntry(JsonElement payload, CallerIdentity caller)
    {
        if (!_authorizationPolicy.CanMutate(caller))
        {
            return new BootOperationResponse(false, "access_denied", "The caller is not authorized to change boot configuration.", null);
        }

        var request = Deserialize<SetDefaultEntryRequest>(payload, ContractsJsonContext.Default.SetDefaultEntryRequest);
        if (request is null || string.IsNullOrWhiteSpace(request.EntryId))
        {
            return InvalidRequest("The set_default_entry payload must include a boot entry id.");
        }

        return _bootConfigurationService.SetDefaultEntry(request);
    }

    private BootOperationResponse RouteSetTimeout(JsonElement payload, CallerIdentity caller)
    {
        if (!_authorizationPolicy.CanMutate(caller))
        {
            return new BootOperationResponse(false, "access_denied", "The caller is not authorized to change boot configuration.", null);
        }

        var request = Deserialize<SetTimeoutRequest>(payload, ContractsJsonContext.Default.SetTimeoutRequest);
        if (request is null)
        {
            return InvalidRequest("The set_timeout payload was missing or invalid.");
        }

        return _bootConfigurationService.SetTimeout(request);
    }

    private static T? Deserialize<T>(JsonElement payload, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            return JsonSerializer.Deserialize(payload, typeInfo);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static BootOperationResponse InvalidRequest(string message) =>
        new(false, "invalid_request", message, null);
}
