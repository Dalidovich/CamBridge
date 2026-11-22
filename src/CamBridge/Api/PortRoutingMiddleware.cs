using CamBridge.Configuration;

namespace CamBridge.Api;

public sealed class PortRoutingMiddleware
{
    private static readonly string[] LoopbackHosts = ["127.0.0.1", "localhost", "[::1]"];

    private readonly RequestDelegate next;
    private readonly AppSettings settings;

    public PortRoutingMiddleware(RequestDelegate next, AppSettings settings)
    {
        this.next = next;
        this.settings = settings;
    }

    public Task InvokeAsync(HttpContext context)
    {
        var onApiPort = context.Connection.LocalPort == settings.ApiPort;
        var path = context.Request.Path;
        var isApiPath = path.StartsWithSegments("/api") || path.StartsWithSegments("/mcp");

        if (onApiPort != isApiPath)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        if (onApiPort && !IsLoopbackHost(context.Request.Host.Host))
        {
            context.Response.StatusCode = StatusCodes.Status421MisdirectedRequest;
            return Task.CompletedTask;
        }

        return next(context);
    }

    private static bool IsLoopbackHost(string host) =>
        LoopbackHosts.Any(candidate => string.Equals(candidate, host, StringComparison.OrdinalIgnoreCase));
}
