using CamBridge.Services;

namespace CamBridge.Api;

public static class CameraApiEndpoints
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var api = routes.MapGroup("/api");

        api.MapGet("/status", (CameraService camera) => Results.Json(camera.GetStatus()));

        api.MapGet("/snapshot", (CameraService camera, double? quality, int? maxWidth, CancellationToken cancellationToken) =>
            ApiErrorMapper.ExecuteAsync(async () =>
            {
                var image = await camera.TakeSnapshotAsync(quality, maxWidth, cancellationToken);
                return Results.File(image.Data, image.MimeType);
            }));

        api.MapGet("/stream", StreamAsync);

        api.MapPost("/torch", (CameraService camera, TorchRequest request, CancellationToken cancellationToken) =>
            ApiErrorMapper.ExecuteAsync(async () =>
            {
                var on = await camera.SetTorchAsync(request.On, cancellationToken);
                return Results.Json(new { ok = true, torch = on });
            }));

        api.MapPost("/zoom", (CameraService camera, ZoomRequest request, CancellationToken cancellationToken) =>
            ApiErrorMapper.ExecuteAsync(async () =>
            {
                var (value, range) = await camera.SetZoomAsync(request.Value, cancellationToken);
                return Results.Json(new { ok = true, zoom = value, min = range.Min, max = range.Max, step = range.Step });
            }));

        api.MapGet("/clip", (CameraService camera, int? seconds, CancellationToken cancellationToken) =>
            ApiErrorMapper.ExecuteAsync(async () =>
            {
                var clip = await camera.RecordClipAsync(seconds ?? 5, cancellationToken);
                var fileName = $"clip-{DateTime.Now:yyyyMMdd-HHmmss}{clip.FileExtension}";
                return Results.File(clip.Data, clip.MimeType, fileName);
            }));
    }

    private static async Task StreamAsync(HttpContext context, CameraService camera, int? fps, double? quality, int? maxWidth)
    {
        StreamSubscription subscription;
        try
        {
            subscription = await camera.OpenStreamAsync(fps, quality, maxWidth);
        }
        catch (Exception ex) when (ApiErrorMapper.TryMap(ex, out var error))
        {
            await error.ExecuteAsync(context);
            return;
        }

        await using (subscription)
        {
            await MjpegStreamWriter.WriteAsync(context, subscription);
        }
    }
}
