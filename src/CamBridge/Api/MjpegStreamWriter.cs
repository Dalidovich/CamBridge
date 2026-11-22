using System.Text;
using CamBridge.Services;
using Microsoft.AspNetCore.Http.Features;

namespace CamBridge.Api;

public static class MjpegStreamWriter
{
    private const string Boundary = "frame";
    private static readonly byte[] FrameTrailer = "\r\n"u8.ToArray();

    public static async Task WriteAsync(HttpContext context, StreamSubscription subscription)
    {
        var response = context.Response;
        var cancellationToken = context.RequestAborted;
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = $"multipart/x-mixed-replace; boundary={Boundary}";
        response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        response.Headers.Pragma = "no-cache";
        await response.StartAsync(cancellationToken);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await subscription.WaitNextFrameAsync(cancellationToken);
                if (frame is null)
                {
                    return;
                }

                var header = Encoding.ASCII.GetBytes(
                    $"--{Boundary}\r\nContent-Type: image/jpeg\r\nContent-Length: {frame.Length}\r\n\r\n");
                await response.Body.WriteAsync(header, cancellationToken);
                await response.Body.WriteAsync(frame, cancellationToken);
                await response.Body.WriteAsync(FrameTrailer, cancellationToken);
                await response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
        }
    }
}
