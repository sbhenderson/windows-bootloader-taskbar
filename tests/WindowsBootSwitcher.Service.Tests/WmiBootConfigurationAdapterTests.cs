using System.Reflection;
using WindowsBootSwitcher.Service.Boot;
using Xunit;

namespace WindowsBootSwitcher.Service.Tests;

public sealed class WmiBootConfigurationAdapterTests
{
    [Fact]
    public void EnsureSuccess_throws_when_wmi_returns_no_out_parameters()
    {
        var method = typeof(WmiBootConfigurationAdapter).GetMethod("EnsureSuccess", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var exception = Assert.Throws<TargetInvocationException>(() => method!.Invoke(null, new object?[] { null, "SetDefaultObject" }));
        Assert.IsType<BootConfigurationException>(exception.InnerException);
    }
}
