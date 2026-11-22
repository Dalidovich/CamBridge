using CamBridge.Infrastructure;
using CamBridge.Models;
using CamBridge.Services;

namespace CamBridge.Api;

public static class PhoneEndpoints
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/", ServeIndex);
        routes.MapGet("/ws/status", (PhoneSessionManager sessions, string? t) => ToResult(sessions.Check(t)));
        routes.Map("/ws", AcceptPhoneAsync);
    }

    private static IResult ServeIndex(HttpContext context, EmbeddedWebAssets assets)
    {
        var index = assets.IndexPage;
        if (!index.Exists)
        {
            return Results.NotFound();
        }

        context.Response.Headers.CacheControl = "no-cache";
        return Results.Stream(index.CreateReadStream(), "text/html; charset=utf-8");
    }

    private static async Task AcceptPhoneAsync(HttpContext context, PhoneSessionManager sessions)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var check = sessions.TryReserve(context.Request.Query["t"]);
        if (check != PairingCheckResult.Accepted)
        {
            await ToResult(check).ExecuteAsync(context);
            return;
        }

        try
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var ip = context.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "unknown";
            var userAgent = context.Request.Headers.UserAgent.ToString();
            await sessions.RunSessionAsync(socket, ip, userAgent);
        }
        finally
        {
            sessions.ReleaseReservation();
        }
    }

    private static IResult ToResult(PairingCheckResult check) => check switch
    {
        PairingCheckResult.Accepted => Results.Json(new { ok = true, status = "available" }),
        PairingCheckResult.Busy => Results.Json(
            new { ok = false, status = "busy", error = "Another phone is already connected." },
            statusCode: StatusCodes.Status409Conflict),
        _ => Results.Json(
            new { ok = false, status = "invalid_token", error = "This pairing link is invalid or expired." },
            statusCode: StatusCodes.Status403Forbidden)
    };
}
