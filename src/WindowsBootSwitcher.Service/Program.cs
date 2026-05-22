using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WindowsBootSwitcher.Service.Boot;
using WindowsBootSwitcher.Service.Diagnostics;
using WindowsBootSwitcher.Service.Hosting;
using WindowsBootSwitcher.Service.Ipc;
using WindowsBootSwitcher.Service.Security;

namespace WindowsBootSwitcher.Service;

internal static class Program
{
    private static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService(options => options.ServiceName = "WindowsBootSwitcher");
        builder.Services.AddSingleton<IBootConfigurationAdapter, WmiBootConfigurationAdapter>();
        builder.Services.AddSingleton<BootConfigurationService>();
        builder.Services.AddSingleton<WindowsIdentityInspector>();
        builder.Services.AddSingleton<CallerAuthorizationPolicy>();
        builder.Services.AddSingleton<EventLogWriter>();
        builder.Services.AddSingleton<BootCommandRouter>();
        builder.Services.AddSingleton<NamedPipeBootSwitcherServer>();
        builder.Services.AddHostedService<WindowsBootSwitcherWorker>();

        using var host = builder.Build();
        host.Run();
    }
}
