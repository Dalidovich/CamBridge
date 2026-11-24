namespace CamBridge.Models;

public sealed record CameraStatus(
    bool Connected,
    string? UserAgent,
    string? Ip,
    DateTimeOffset? ConnectedAt,
    PhoneCapabilities? Capabilities,
    bool Torch,
    double? Zoom,
    bool Streaming,
    bool FrameLocked,
    DateTimeOffset? FrameLockedAt);
