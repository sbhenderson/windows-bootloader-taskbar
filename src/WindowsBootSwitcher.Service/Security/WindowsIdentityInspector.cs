using System.Linq;
using System.Security.Principal;

namespace WindowsBootSwitcher.Service.Security;

public class WindowsIdentityInspector
{
    public virtual CallerIdentity Inspect(WindowsIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var principal = new WindowsPrincipal(identity);
        var isAdministrator = principal.IsInRole(WindowsBuiltInRole.Administrator);
        var isLocalInteractiveUser = HasSid(identity, WellKnownSidType.InteractiveSid);

        return new CallerIdentity(isLocalInteractiveUser, isAdministrator);
    }

    private static bool HasSid(WindowsIdentity identity, WellKnownSidType sidType)
    {
        var sid = new SecurityIdentifier(sidType, null);
        return identity.Groups?.Cast<SecurityIdentifier>().Any(group => group.Equals(sid)) == true;
    }
}
