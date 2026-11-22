using System.Net;
using CamBridge.Configuration;
using CamBridge.Infrastructure;

namespace CamBridge.Extensions;

public static class WebApplicationBuilderExtensions
{
    public static WebApplicationBuilder ConfigureCamBridge(
        this WebApplicationBuilder builder,
        AppSettings settings,
        NetworkSelection network,
        CertificateProvider certificates)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.AspNetCore.Server.Kestrel", LogLevel.Error);

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Listen(IPAddress.Any, settings.PhonePort, listen => listen.UseHttps(certificates.Certificate));
            options.Listen(IPAddress.Loopback, settings.ApiPort);
        });

        builder.Services.AddCamBridge(settings, network, certificates);
        return builder;
    }
}
