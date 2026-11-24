using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using CamBridge.Configuration;
using CamBridge.Exceptions;
using CamBridge.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CamBridge.Mcp;

[McpServerToolType]
public sealed class CameraTools
{
    private static readonly JsonSerializerOptions StatusSerializerOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly CameraService camera;
    private readonly AppSettings settings;

    public CameraTools(CameraService camera, AppSettings settings)
    {
        this.camera = camera;
        this.settings = settings;
    }

    [McpServerTool(Name = "get_status", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Returns the CamBridge phone camera status as JSON: whether a phone is connected, its user agent, IP address, connection time, camera capabilities (torch, zoom range, resolution, recording formats), current torch state and zoom value, and whether the user has locked a frame on the phone (frameLocked, frameLockedAt).")]
    public CallToolResult GetStatus()
    {
        var json = JsonSerializer.Serialize(camera.GetStatus(), StatusSerializerOptions);
        return Text(json);
    }

    [McpServerTool(Name = "take_snapshot", ReadOnly = true, OpenWorld = false)]
    [Description("Takes a photo with the connected phone camera and returns it as a JPEG image. If the user has locked a frame on the phone, returns that locked frame instead of a live photo (quality and max_width are ignored).")]
    public Task<CallToolResult> TakeSnapshot(
        [Description("JPEG quality from 0.1 to 1. Defaults to the JpegQuality setting.")] double? quality = null,
        [Description("Maximum image width in pixels; the image is scaled down keeping the aspect ratio. Defaults to the MaxFrameWidth setting.")] int? max_width = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            var lockedAt = camera.FrameLockedAt;
            var image = await camera.TakeSnapshotAsync(quality, max_width ?? settings.MaxFrameWidth, cancellationToken);
            return ImageResult([ImageContentBlock.FromBytes(image.Data, image.MimeType)], lockedAt);
        });
    }

    [McpServerTool(Name = "capture_frames", ReadOnly = true, OpenWorld = false)]
    [Description("Captures a sequence of JPEG frames from the connected phone camera at a fixed interval and returns them in capture order. Useful for observing motion or changes over time. If the user has locked a frame on the phone, returns only that single locked frame.")]
    public Task<CallToolResult> CaptureFrames(
        [Description("Number of frames to capture, 1 to 10.")] int count,
        [Description("Delay between frames in milliseconds, 100 to 10000. Defaults to 1000.")] int interval_ms = MediaLimits.DefaultCaptureIntervalMs,
        [Description("Maximum frame width in pixels. Defaults to 1280.")] int? max_width = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            var lockedAt = camera.FrameLockedAt;
            if (lockedAt is not null)
            {
                var locked = await camera.TakeSnapshotAsync(null, null, cancellationToken);
                return ImageResult([ImageContentBlock.FromBytes(locked.Data, locked.MimeType)], lockedAt);
            }

            var frameCount = Math.Clamp(count, MediaLimits.MinFrameCount, MediaLimits.MaxFrameCount);
            var interval = TimeSpan.FromMilliseconds(Math.Clamp(interval_ms, MediaLimits.MinIntervalMs, MediaLimits.MaxIntervalMs));
            var width = max_width ?? MediaLimits.DefaultCaptureWidth;
            var content = new List<ContentBlock>(frameCount);
            for (var index = 0; index < frameCount; index++)
            {
                if (index > 0)
                {
                    await Task.Delay(interval, cancellationToken);
                }

                var frame = await camera.TakeSnapshotAsync(null, width, cancellationToken);
                content.Add(ImageContentBlock.FromBytes(frame.Data, frame.MimeType));
            }

            return new CallToolResult { Content = content };
        });
    }

    [McpServerTool(Name = "set_torch", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Turns the phone camera torch (flashlight) on or off. Fails while a frame is locked on the phone.")]
    public Task<CallToolResult> SetTorch(
        [Description("True to turn the torch on, false to turn it off.")] bool on,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            var state = await camera.SetTorchAsync(on, cancellationToken);
            return Text(state ? "Torch is on." : "Torch is off.");
        });
    }

    [McpServerTool(Name = "set_zoom", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Sets the phone camera zoom level. The value is clamped to the range supported by the camera; the result reports the applied value and the supported range. Fails while a frame is locked on the phone.")]
    public Task<CallToolResult> SetZoom(
        [Description("Requested zoom level, for example 1 for no zoom or 2 for 2x.")] double value,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            var (applied, range) = await camera.SetZoomAsync(value, cancellationToken);
            return Text(string.Create(
                CultureInfo.InvariantCulture,
                $"Zoom set to {applied:0.##} (requested {value:0.##}, supported range {range.Min:0.##} to {range.Max:0.##}, step {range.Step:0.###})."));
        });
    }

    [McpServerTool(Name = "record_clip", ReadOnly = true, OpenWorld = false)]
    [Description("Records a short video clip with the phone camera and returns it as an embedded binary resource (video/mp4 or video/webm depending on the phone). For large clips or saving to disk, prefer the REST endpoint GET http://127.0.0.1:<ApiPort>/api/clip?seconds=N, which returns the video file directly. Fails while a frame is locked on the phone.")]
    public Task<CallToolResult> RecordClip(
        [Description("Clip length in seconds, 1 to 30.")] int seconds,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            var clip = await camera.RecordClipAsync(seconds, cancellationToken);
            var uri = $"cambridge://clips/{DateTime.Now:yyyyMMdd-HHmmss}{clip.FileExtension}";
            return new CallToolResult
            {
                Content =
                [
                    new EmbeddedResourceBlock { Resource = BlobResourceContents.FromBytes(clip.Data, uri, clip.MimeType) },
                    new TextContentBlock { Text = $"Recorded {Math.Clamp(seconds, MediaLimits.MinClipSeconds, MediaLimits.MaxClipSeconds)} s clip: {clip.MimeType}, {clip.Data.Length} bytes." }
                ]
            };
        });
    }

    private static async Task<CallToolResult> ExecuteAsync(Func<Task<CallToolResult>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is PhoneNotConnectedException or PhoneTimeoutException or PhoneCommandException or FrameLockedException)
        {
            return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = ex.Message }] };
        }
    }

    private static CallToolResult ImageResult(List<ContentBlock> images, DateTimeOffset? lockedAt)
    {
        if (lockedAt is not null)
        {
            images.Add(new TextContentBlock
            {
                Text = $"This is the frame locked on the phone at {lockedAt.Value:yyyy-MM-dd HH:mm:ss}. Live camera access is unavailable until it is unlocked on the phone."
            });
        }

        return new CallToolResult { Content = images };
    }

    private static CallToolResult Text(string text) => new() { Content = [new TextContentBlock { Text = text }] };
}
