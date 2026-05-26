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
        _ = builder.Services.AddWindowsService(options => options.ServiceName = "WindowsBootSwitcher")
               .AddSingleton<IBootConfigurationAdapter, WmiBootConfigurationAdapter>()
               .AddSingleton<BootConfigurationService>()
               .AddSingleton<WindowsIdentityInspector>()
               .AddSingleton<CallerAuthorizationPolicy>()
               .AddSingleton<EventLogWriter>()
               .AddSingleton<BootCommandRouter>()
               .AddSingleton<NamedPipeBootSwitcherServer>()
               .AddHostedService<WindowsBootSwitcherWorker>();

        using var host = builder.Build();
        host.Run();
    }
}
