namespace WindowsBootSwitcher.Service.Security;

public sealed class CallerAuthorizationPolicy
{
    public bool CanRead(CallerIdentity caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        return caller.IsLocalInteractiveUser || caller.IsLocalAdministrator;
    }

    public bool CanMutate(CallerIdentity caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        return caller.IsLocalAdministrator;
    }
}

public sealed record CallerIdentity(bool IsLocalInteractiveUser, bool IsLocalAdministrator);
