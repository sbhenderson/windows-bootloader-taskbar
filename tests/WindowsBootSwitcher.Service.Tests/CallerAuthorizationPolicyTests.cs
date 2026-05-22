using WindowsBootSwitcher.Service.Security;
using Xunit;

namespace WindowsBootSwitcher.Service.Tests;

public sealed class CallerAuthorizationPolicyTests
{
    private readonly CallerAuthorizationPolicy _policy = new();

    [Fact]
    public void CanRead_allows_local_interactive_users()
    {
        var caller = new CallerIdentity(IsLocalInteractiveUser: true, IsLocalAdministrator: false);

        Assert.True(_policy.CanRead(caller));
    }

    [Fact]
    public void CanRead_allows_local_administrators()
    {
        var caller = new CallerIdentity(IsLocalInteractiveUser: false, IsLocalAdministrator: true);

        Assert.True(_policy.CanRead(caller));
    }

    [Fact]
    public void CanMutate_requires_local_administrator_membership()
    {
        var readOnlyCaller = new CallerIdentity(IsLocalInteractiveUser: true, IsLocalAdministrator: false);
        var adminCaller = new CallerIdentity(IsLocalInteractiveUser: true, IsLocalAdministrator: true);

        Assert.False(_policy.CanMutate(readOnlyCaller));
        Assert.True(_policy.CanMutate(adminCaller));
    }

    [Fact]
    public void CanRead_denies_non_interactive_non_admin_callers()
    {
        var caller = new CallerIdentity(IsLocalInteractiveUser: false, IsLocalAdministrator: false);

        Assert.False(_policy.CanRead(caller));
    }
}
