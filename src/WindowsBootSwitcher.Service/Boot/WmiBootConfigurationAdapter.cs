using System.Collections.Immutable;
using System.Management;

namespace WindowsBootSwitcher.Service.Boot;

public sealed class WmiBootConfigurationAdapter : IBootConfigurationAdapter
{
    private const string RootNamespace = @"root\WMI";
    private const string BcdStoreClassName = "BcdStore";
    private const string BcdObjectClassName = "BcdObject";
    private const string BootManagerObjectId = "{9dea862c-5cdd-4e70-acc1-f32b344d4795}";
    private const uint WindowsOsLoaderApplicationType = 0x10200003;
    private const uint BootManagerTimeoutElementType = 0x23000003;

    public BootConfigurationSnapshot ReadState()
    {
        using var store = GetStore();
        var defaultEntryId = ReadDefaultEntryId(store);
        var timeoutSeconds = ReadTimeoutSeconds();
        var entries = ReadEntries(defaultEntryId);

        return new BootConfigurationSnapshot(defaultEntryId, timeoutSeconds, entries);
    }

    public void SetDefaultEntry(string entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId))
        {
            throw new BootConfigurationException("invalid_entry_id", "Boot entry id must be provided.");
        }

        using var store = GetStore();
        using var inParameters = store.GetMethodParameters("SetDefaultObject");
        inParameters["Id"] = entryId;

        using var outParameters = store.InvokeMethod("SetDefaultObject", inParameters, null);
        EnsureSuccess(outParameters, "SetDefaultObject");
    }

    public void SetTimeout(int timeoutSeconds)
    {
        if (timeoutSeconds < 0)
        {
            throw new BootConfigurationException("invalid_timeout", "Boot timeout must be zero or greater.");
        }

        using var bootManager = GetBootManagerObject();
        using var inParameters = bootManager.GetMethodParameters("SetIntegerElement");
        inParameters["Type"] = BootManagerTimeoutElementType;
        inParameters["Integer"] = (ulong)timeoutSeconds;

        using var outParameters = bootManager.InvokeMethod("SetIntegerElement", inParameters, null);
        EnsureSuccess(outParameters, "SetIntegerElement");
    }

    private static ManagementObject GetStore()
    {
        return GetSingleObject($"SELECT * FROM {BcdStoreClassName}", BcdStoreClassName);
    }

    private static ManagementObject GetBootManagerObject()
    {
        return GetSingleObject($"SELECT * FROM {BcdObjectClassName}", BcdObjectClassName, objectId: BootManagerObjectId);
    }

    private static string? ReadDefaultEntryId(ManagementObject store)
    {
        return store["DefaultObject"]?.ToString();
    }

    private static int ReadTimeoutSeconds()
    {
        using var bootManager = GetBootManagerObject();
        using var inParameters = bootManager.GetMethodParameters("GetIntegerElement");
        inParameters["Type"] = BootManagerTimeoutElementType;

        using var outParameters = bootManager.InvokeMethod("GetIntegerElement", inParameters, null);
        EnsureSuccess(outParameters, "GetIntegerElement");

        return Convert.ToInt32(outParameters?["Integer"], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static ImmutableArray<BootConfigurationEntry> ReadEntries(string? defaultEntryId)
    {
        using var searcher = new ManagementObjectSearcher(
            new ManagementScope(RootNamespace),
            new ObjectQuery($"SELECT Id, Description, ApplicationType FROM {BcdObjectClassName}"));

        var builder = ImmutableArray.CreateBuilder<BootConfigurationEntry>();

        foreach (ManagementObject obj in searcher.Get())
        {
            var id = obj["Id"]?.ToString();
            var description = obj["Description"]?.ToString();

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(description))
            {
                continue;
            }

            var applicationType = ReadUInt32(obj, "ApplicationType");
            if (applicationType != WindowsOsLoaderApplicationType)
            {
                continue;
            }

            builder.Add(new BootConfigurationEntry(
                id,
                description,
                string.Equals(id, defaultEntryId, StringComparison.OrdinalIgnoreCase)));
        }

        return builder.ToImmutable();
    }

    private static uint ReadUInt32(ManagementBaseObject obj, string propertyName)
    {
        var value = obj[propertyName];
        if (value is null)
        {
            throw new BootConfigurationException("missing_property", $"BCD property '{propertyName}' was not present.");
        }

        return Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static ManagementObject GetSingleObject(string query, string className, string? objectId = null)
    {
        using var searcher = new ManagementObjectSearcher(new ManagementScope(RootNamespace), new ObjectQuery(query));

        foreach (ManagementObject obj in searcher.Get())
        {
            if (objectId is null)
            {
                return obj;
            }

            var id = obj["Id"]?.ToString();
            if (string.Equals(id, objectId, StringComparison.OrdinalIgnoreCase))
            {
                return obj;
            }
        }

        throw new BootConfigurationException("bcd_object_not_found", $"Unable to locate BCD {className}.");
    }

    private static void EnsureSuccess(ManagementBaseObject? outParameters, string methodName)
    {
        var returnValue = outParameters?["ReturnValue"];
        var status = returnValue is null
            ? 0u
            : Convert.ToUInt32(returnValue, System.Globalization.CultureInfo.InvariantCulture);

        if (status == 0)
        {
            return;
        }

        throw new BootConfigurationException(
            "wmi_error",
            $"BCD operation '{methodName}' failed with status code {status}.");
    }
}
