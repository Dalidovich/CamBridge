using CamBridge.Configuration;
using CamBridge.Infrastructure;
using CamBridge.Mcp;
using CamBridge.Services;
using CamBridge.Terminal;
using Microsoft.AspNetCore.DataProtection;
using ModelContextProtocol.Protocol;

namespace CamBridge.Extensions;

public static class ServiceCollectionExtensions
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    public static IServiceCollection AddCamBridge(
        this IServiceCollection services,
        AppSettings settings,
        NetworkSelection network,
        CertificateProvider certificates)
    {
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.Configure<HostOptions>(options => options.ShutdownTimeout = ShutdownTimeout);

        services.AddSingleton(settings);
        services.AddSingleton(network);
        services.AddSingleton(certificates);
        services.AddSingleton<EmbeddedWebAssets>();
        services.AddSingleton<PairingTokenService>();
        services.AddSingleton<PhoneSessionManager>();
        services.AddSingleton<CameraService>();
        services.AddSingleton<ConsolePresenter>();
        services.AddHostedService<KeyboardListener>();

        services
            .AddMcpServer(options => options.ServerInfo = new Implementation { Name = "CamBridge", Version = "1.0.0" })
            .WithHttpTransport(options => options.Stateless = true)
            .WithTools<CameraTools>();

        return services;
    }
}
