using CamBridge.Configuration;
using CamBridge.Exceptions;
using CamBridge.Models;

namespace CamBridge.Services;

public sealed class CameraService
{
    private static readonly TimeSpan SnapshotTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ControlTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RecordTimeoutMargin = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MinCachedFrameAge = TimeSpan.FromMilliseconds(500);

    private readonly PhoneSessionManager sessions;
    private readonly AppSettings settings;

    public CameraService(PhoneSessionManager sessions, AppSettings settings)
    {
        this.sessions = sessions;
        this.settings = settings;
    }

    public CameraStatus GetStatus()
    {
        var session = sessions.Current;
        if (session is null)
        {
            return new CameraStatus(false, null, null, null, null, false, null, false, false, null);
        }

        var lockedAt = session.Frames.LockedAt;
        return new CameraStatus(
            true,
            session.UserAgent,
            session.Ip,
            session.ConnectedAt,
            session.Capabilities,
            session.TorchOn,
            session.ZoomValue,
            session.Frames.IsActive,
            lockedAt is not null,
            lockedAt);
    }

    public DateTimeOffset? FrameLockedAt => sessions.Current?.Frames.LockedAt;

    public async Task<MediaPayload> TakeSnapshotAsync(double? quality, int? maxWidth, CancellationToken cancellationToken)
    {
        var session = RequireSession();
        var locked = session.Frames.LockedFrame;
        if (locked is not null)
        {
            return new MediaPayload(locked, "image/jpeg");
        }

        var cached = TryGetCachedFrame(session);
        if (cached is not null)
        {
            return new MediaPayload(cached, "image/jpeg");
        }

        var effectiveQuality = MediaLimits.ClampQuality(quality, settings.JpegQuality);
        var effectiveWidth = MediaLimits.ClampWidth(maxWidth, settings.MaxFrameWidth);
        var reply = await session.RequestAsync(
            "snapshot",
            id => new { type = "snapshot", id, quality = effectiveQuality, maxWidth = effectiveWidth },
            SnapshotTimeout,
            cancellationToken);
        return RequireMedia(reply, "Snapshot");
    }

    public async Task<bool> SetTorchAsync(bool on, CancellationToken cancellationToken)
    {
        var session = RequireUnlockedSession("Torch control");
        if (session.Capabilities is { Torch: false })
        {
            throw new PhoneCommandException("The phone camera does not support the torch.");
        }

        var reply = await session.RequestAsync("torch", id => new { type = "torch", id, on }, ControlTimeout, cancellationToken);
        EnsureOk(reply, "Torch");
        session.TorchOn = on;
        return on;
    }

    public async Task<(double Value, ZoomCapability Range)> SetZoomAsync(double value, CancellationToken cancellationToken)
    {
        var session = RequireUnlockedSession("Zoom control");
        var range = session.Capabilities?.Zoom
            ?? throw new PhoneCommandException("The phone camera does not support zoom.");
        var clamped = range.Clamp(double.IsNaN(value) ? range.Min : value);
        var reply = await session.RequestAsync("zoom", id => new { type = "zoom", id, value = clamped }, ControlTimeout, cancellationToken);
        EnsureOk(reply, "Zoom");
        session.ZoomValue = clamped;
        return (clamped, range);
    }

    public async Task<MediaPayload> RecordClipAsync(int seconds, CancellationToken cancellationToken)
    {
        var session = RequireUnlockedSession("Recording");
        var duration = Math.Clamp(seconds, MediaLimits.MinClipSeconds, MediaLimits.MaxClipSeconds);
        var reply = await session.RequestAsync(
            "record",
            id => new { type = "record", id, seconds = duration },
            TimeSpan.FromSeconds(duration) + RecordTimeoutMargin,
            cancellationToken);
        return RequireMedia(reply, "Recording");
    }

    public Task<StreamSubscription> OpenStreamAsync(int? fps, double? quality, int? maxWidth)
    {
        var session = RequireSession();
        var parameters = new StreamParameters(
            MediaLimits.ClampFps(fps, settings.StreamFps),
            MediaLimits.ClampQuality(quality, settings.JpegQuality),
            MediaLimits.ClampWidth(maxWidth, settings.MaxFrameWidth));
        return StreamSubscription.OpenAsync(session, parameters);
    }

    private static byte[]? TryGetCachedFrame(PhoneSession session)
    {
        var effective = session.Frames.Effective;
        if (effective is null)
        {
            return null;
        }

        var frameInterval = TimeSpan.FromSeconds(2.0 / effective.Fps);
        return session.Frames.TryGetFreshFrame(frameInterval > MinCachedFrameAge ? frameInterval : MinCachedFrameAge);
    }

    private PhoneSession RequireSession() => sessions.Current ?? throw new PhoneNotConnectedException();

    private PhoneSession RequireUnlockedSession(string operation)
    {
        var session = RequireSession();
        return session.Frames.LockedFrame is null ? session : throw new FrameLockedException(operation);
    }

    private static MediaPayload RequireMedia(PhoneReply reply, string operation)
    {
        EnsureOk(reply, operation);
        return reply.Media ?? throw new PhoneCommandException($"{operation} returned no data.");
    }

    private static void EnsureOk(PhoneReply reply, string operation)
    {
        if (!reply.Ok)
        {
            throw new PhoneCommandException($"{operation} failed on the phone: {reply.Error ?? "unknown error"}");
        }
    }
}
