using CamBridge.Api;
using CamBridge.Infrastructure;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.StaticFiles;

namespace CamBridge.Extensions;

public static class WebApplicationExtensions
{
    private static readonly TimeSpan WebSocketKeepAlive = TimeSpan.FromSeconds(15);

    public static WebApplication UseCamBridge(this WebApplication app)
    {
        app.UseMiddleware<PortRoutingMiddleware>();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = WebSocketKeepAlive });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = app.Services.GetRequiredService<EmbeddedWebAssets>().FileProvider,
            ContentTypeProvider = new FileExtensionContentTypeProvider(),
            OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache"
        });

        PhoneEndpoints.Map(app);
        CameraApiEndpoints.Map(app);
        app.MapMcp("/mcp");
        return app;
    }

    public static async Task<bool> TryStartAsync(this WebApplication app)
    {
        try
        {
            await app.StartAsync();
            return true;
        }
        catch (Exception ex) when (ex is AddressInUseException || ex.InnerException is AddressInUseException)
        {
            await Console.Error.WriteLineAsync($"Error: a required port is already in use ({ex.Message}).");
            return false;
        }
    }
}
