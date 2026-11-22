using System.Text;
using CamBridge.Configuration;
using CamBridge.Extensions;
using CamBridge.Infrastructure;
using CamBridge.Terminal;

namespace CamBridge;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var settings = SettingsStore.Load();
        var portConflict = PortAvailability.DescribeConflict(settings);
        if (portConflict is not null)
        {
            await Console.Error.WriteLineAsync(portConflict);
            Environment.ExitCode = 1;
            return;
        }

        var network = LanAddressResolver.Resolve(settings.PreferredIp);
        using var certificates = CertificateProvider.Create(network);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = PortablePaths.AppRoot,
            EnvironmentName = Environments.Production
        });
        builder.ConfigureCamBridge(settings, network, certificates);

        await using var app = builder.Build();
        app.UseCamBridge();

        if (!await app.TryStartAsync())
        {
            Environment.ExitCode = 1;
            return;
        }

        app.Services.GetRequiredService<ConsolePresenter>().PrintStartup();
        await app.WaitForShutdownAsync();
    }
}
