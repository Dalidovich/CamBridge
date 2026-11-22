namespace CamBridge.Models;

public sealed record PhoneCapabilities(
    bool Torch,
    ZoomCapability? Zoom,
    int Width,
    int Height,
    IReadOnlyList<string> RecordMimeTypes);
