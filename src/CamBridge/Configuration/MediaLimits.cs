namespace CamBridge.Configuration;

public static class MediaLimits
{
    public const double MinQuality = 0.1;
    public const double MaxQuality = 1.0;
    public const int MinFps = 1;
    public const int MaxFps = 30;
    public const int MinWidth = 160;
    public const int MaxWidth = 4096;
    public const int MinClipSeconds = 1;
    public const int MaxClipSeconds = 30;
    public const int MinFrameCount = 1;
    public const int MaxFrameCount = 10;
    public const int MinIntervalMs = 100;
    public const int MaxIntervalMs = 10000;
    public const int DefaultCaptureWidth = 1280;
    public const int DefaultCaptureIntervalMs = 1000;

    public static double ClampQuality(double? value, double fallback)
    {
        var quality = value is null || double.IsNaN(value.Value) ? fallback : value.Value;
        return Math.Clamp(quality, MinQuality, MaxQuality);
    }

    public static int ClampWidth(int? value, int fallback) => Math.Clamp(value ?? fallback, MinWidth, MaxWidth);

    public static int ClampFps(int? value, int fallback) => Math.Clamp(value ?? fallback, MinFps, MaxFps);
}
