using System.Collections.Immutable;
using System.Management;

namespace WindowsBootSwitcher.Service.Boot;

/// <summary>
/// Reads and writes the system BCD store through the WMI BCD provider in <c>root\WMI</c>.
/// </summary>
/// <remarks>
/// The provider exposes <c>BcdStore</c> and <c>BcdObject</c>. There is no <c>SetDefaultObject</c>
/// or <c>GetIntegerElement</c> method: the default entry and the menu timeout are both
/// <em>elements</em> of the well known boot manager object, read with <c>GetElement</c> and written
/// with <c>SetObjectElement</c> / <c>SetIntegerElement</c>.
/// </remarks>
public sealed class WmiBootConfigurationAdapter : IBootConfigurationAdapter
{
    private const string RootNamespace = @"root\WMI";
    private const string BcdStoreClassName = "BcdStore";
    private const string BootManagerObjectId = "{9dea862c-5cdd-4e70-acc1-f32b344d4795}";

    /// <summary>Value of <c>BcdObject.Type</c> for a Windows OS loader application object.</summary>
    private const uint WindowsOsLoaderObjectType = 0x10200003;

    /// <summary><c>BcdLibraryString_Description</c>: the friendly entry name.</summary>
    private const uint DescriptionElementType = 0x12000004;

    /// <summary><c>BcdBootMgrObject_DefaultObject</c>: an object (GUID) element, not an integer.</summary>
    private const uint DefaultObjectElementType = 0x23000003;

    /// <summary><c>BcdBootMgrInteger_Timeout</c>: the boot menu timeout in seconds.</summary>
    private const uint TimeoutElementType = 0x25000004;

    public BootConfigurationSnapshot ReadState()
    {
        try
        {
            using var store = OpenSystemStore();
            using var bootManager = OpenObject(store, BootManagerObjectId);

            var defaultEntryId = ReadObjectElementId(bootManager, DefaultObjectElementType);

            // The timeout element is optional; an absent element means "wait for a selection".
            var timeoutElement = ReadIntegerElement(bootManager, TimeoutElementType);
            var timeoutSeconds = timeoutElement is null ? 0 : BcdValueReader.ClampToSeconds(timeoutElement.Value);

            var entries = ReadEntries(store);

            return new BootConfigurationSnapshot(defaultEntryId, timeoutSeconds, entries);
        }
        catch (BootConfigurationException)
        {
            throw;
        }
        catch (Exception exception) when (IsWmiFailure(exception))
        {
            throw new BootConfigurationException("wmi_error", "Failed to read boot configuration from WMI.", exception);
        }
    }

    public void SetDefaultEntry(string entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId))
        {
            throw new BootConfigurationException("invalid_entry_id", "Boot entry id must be provided.");
        }

        try
        {
            using var store = OpenSystemStore();
            using var bootManager = OpenObject(store, BootManagerObjectId);
            using var inParameters = bootManager.GetMethodParameters("SetObjectElement");
            inParameters["Type"] = DefaultObjectElementType;
            inParameters["Id"] = entryId;

            using var outParameters = bootManager.InvokeMethod("SetObjectElement", inParameters, null);
            EnsureSuccess(outParameters, "SetObjectElement");
        }
        catch (BootConfigurationException)
        {
            throw;
        }
        catch (Exception exception) when (IsWmiFailure(exception))
        {
            throw new BootConfigurationException("wmi_error", "Failed to set the default boot entry.", exception);
        }
    }

    public void SetTimeout(int timeoutSeconds)
    {
        if (timeoutSeconds < 0)
        {
            throw new BootConfigurationException("invalid_timeout", "Boot timeout must be zero or greater.");
        }

        try
        {
            using var store = OpenSystemStore();
            using var bootManager = OpenObject(store, BootManagerObjectId);
            using var inParameters = bootManager.GetMethodParameters("SetIntegerElement");
            inParameters["Type"] = TimeoutElementType;
            inParameters["Integer"] = (ulong)timeoutSeconds;

            using var outParameters = bootManager.InvokeMethod("SetIntegerElement", inParameters, null);
            EnsureSuccess(outParameters, "SetIntegerElement");
        }
        catch (BootConfigurationException)
        {
            throw;
        }
        catch (Exception exception) when (IsWmiFailure(exception))
        {
            throw new BootConfigurationException("wmi_error", "Failed to set the boot menu timeout.", exception);
        }
    }

    /// <summary>
    /// Connects with impersonation and enabled privileges, which the BCD provider requires so the
    /// caller's Backup/Restore privileges are available for store access.
    /// </summary>
    private static ManagementScope CreateScope()
    {
        var scope = new ManagementScope(
            RootNamespace,
            new ConnectionOptions
            {
                Impersonation = ImpersonationLevel.Impersonate,
                EnablePrivileges = true
            });

        scope.Connect();
        return scope;
    }

    private static ManagementObject OpenSystemStore()
    {
        var scope = CreateScope();
        using var storeClass = new ManagementClass(scope, new ManagementPath(BcdStoreClassName), null);
        using var inParameters = storeClass.GetMethodParameters("OpenStore");

        // An empty path opens the live system store.
        inParameters["File"] = string.Empty;

        using var outParameters = storeClass.InvokeMethod("OpenStore", inParameters, null);
        EnsureSuccess(outParameters, "OpenStore");
        return ExtractObject(outParameters, "Store", "OpenStore", scope);
    }

    private static ManagementObject OpenObject(ManagementObject store, string objectId)
    {
        using var inParameters = store.GetMethodParameters("OpenObject");
        inParameters["Id"] = objectId;

        using var outParameters = store.InvokeMethod("OpenObject", inParameters, null);
        if (!TryEnsureSuccess(outParameters, "OpenObject"))
        {
            throw new BootConfigurationException(
                "bcd_object_not_found",
                $"Unable to locate BCD object '{objectId}'.");
        }

        return ExtractObject(outParameters, "Object", "OpenObject", store.Scope);
    }

    private static ImmutableArray<BootConfigurationEntry> ReadEntries(ManagementObject store)
    {
        using var inParameters = store.GetMethodParameters("EnumerateObjects");
        inParameters["Type"] = WindowsOsLoaderObjectType;

        using var outParameters = store.InvokeMethod("EnumerateObjects", inParameters, null);
        if (!TryEnsureSuccess(outParameters, "EnumerateObjects") || outParameters is null)
        {
            return [];
        }

        if (outParameters["Objects"] is not ManagementBaseObject[] objects)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<BootConfigurationEntry>();

        foreach (var candidate in objects)
        {
            using (candidate)
            {
                var id = candidate["Id"]?.ToString();
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                // The enumeration is already filtered by type, but the flag is recomputed so a
                // provider that ignores the filter cannot widen what the tray offers.
                var objectType = BcdValueReader.ReadUInt32(candidate["Type"]);
                var isWindowsOsLoader = objectType == WindowsOsLoaderObjectType;

                string? description = null;
                if (candidate is ManagementObject managedCandidate)
                {
                    managedCandidate.Scope = store.Scope;
                    description = ReadStringElement(managedCandidate, DescriptionElementType);
                }

                builder.Add(new BootConfigurationEntry(
                    id,
                    string.IsNullOrWhiteSpace(description) ? id : description,
                    isWindowsOsLoader));
            }
        }

        return builder.ToImmutable();
    }

    private static string? ReadStringElement(ManagementObject bcdObject, uint elementType) =>
        ReadElement(bcdObject, elementType, element => element["String"]?.ToString());

    private static string? ReadObjectElementId(ManagementObject bcdObject, uint elementType) =>
        ReadElement(bcdObject, elementType, element => element["Id"]?.ToString());

    private static ulong? ReadIntegerElement(ManagementObject bcdObject, uint elementType) =>
        ReadElement(bcdObject, elementType, element => BcdValueReader.ReadUInt64(element["Integer"]));

    /// <summary>
    /// Reads a single element. An element that is not present on the object is not an error: the
    /// provider reports it by returning <c>FALSE</c>, and this returns <see langword="default"/>.
    /// </summary>
    private static TResult? ReadElement<TResult>(
        ManagementObject bcdObject,
        uint elementType,
        Func<ManagementBaseObject, TResult?> selector)
    {
        ManagementBaseObject? outParameters;

        try
        {
            using var inParameters = bcdObject.GetMethodParameters("GetElement");
            inParameters["Type"] = elementType;
            outParameters = bcdObject.InvokeMethod("GetElement", inParameters, null);
        }
        catch (ManagementException exception) when (exception.ErrorCode == ManagementStatus.NotFound)
        {
            return default;
        }

        using (outParameters)
        {
            if (!TryEnsureSuccess(outParameters, "GetElement") || outParameters is null)
            {
                return default;
            }

            if (outParameters["Element"] is not ManagementBaseObject element)
            {
                return default;
            }

            using (element)
            {
                return selector(element);
            }
        }
    }

    private static ManagementObject ExtractObject(
        ManagementBaseObject? outParameters,
        string propertyName,
        string methodName,
        ManagementScope scope)
    {
        var raw = outParameters?[propertyName];
        if (raw is not ManagementObject managementObject)
        {
            (raw as IDisposable)?.Dispose();
            throw new BootConfigurationException(
                "wmi_error",
                $"BCD operation '{methodName}' did not return a usable '{propertyName}' object.");
        }

        // Embedded results carry no connection, so reuse the privileged scope for later calls.
        managementObject.Scope = scope;
        return managementObject;
    }

    private static bool IsWmiFailure(Exception exception) =>
        exception is ManagementException
            or System.Runtime.InteropServices.COMException
            or UnauthorizedAccessException;

    /// <summary>
    /// Evaluates the boolean status of a BCD call without throwing when it reports failure.
    /// </summary>
    private static bool TryEnsureSuccess(ManagementBaseObject? outParameters, string methodName)
    {
        if (outParameters is null)
        {
            throw new BootConfigurationException(
                "wmi_error",
                $"BCD operation '{methodName}' returned no output parameters.");
        }

        return BcdValueReader.IsSuccess(outParameters["ReturnValue"], methodName);
    }

    private static void EnsureSuccess(ManagementBaseObject? outParameters, string methodName)
    {
        if (!TryEnsureSuccess(outParameters, methodName))
        {
            throw new BootConfigurationException(
                "wmi_error",
                $"BCD operation '{methodName}' reported failure. Confirm the service is running with administrative privileges.");
        }
    }
}
