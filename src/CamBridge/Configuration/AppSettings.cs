namespace CamBridge.Configuration;

public sealed class AppSettings
{
    public const int DefaultPhonePort = 8443;
    public const int DefaultApiPort = 8780;
    public const double DefaultJpegQuality = 0.85;
    public const int DefaultStreamFps = 10;
    public const int DefaultMaxFrameWidth = 1920;

    public int PhonePort { get; set; } = DefaultPhonePort;

    public int ApiPort { get; set; } = DefaultApiPort;

    public string? PreferredIp { get; set; }

    public double JpegQuality { get; set; } = DefaultJpegQuality;

    public int StreamFps { get; set; } = DefaultStreamFps;

    public int MaxFrameWidth { get; set; } = DefaultMaxFrameWidth;

    public bool Normalize()
    {
        var changed = false;

        if (!IsValidPort(PhonePort))
        {
            PhonePort = DefaultPhonePort;
            changed = true;
        }

        if (!IsValidPort(ApiPort) || ApiPort == PhonePort)
        {
            ApiPort = PhonePort == DefaultApiPort ? DefaultPhonePort + 1 : DefaultApiPort;
            changed = true;
        }

        if (PreferredIp is not null && string.IsNullOrWhiteSpace(PreferredIp))
        {
            PreferredIp = null;
            changed = true;
        }

        if (double.IsNaN(JpegQuality) || JpegQuality < MediaLimits.MinQuality || JpegQuality > MediaLimits.MaxQuality)
        {
            JpegQuality = DefaultJpegQuality;
            changed = true;
        }

        if (StreamFps < MediaLimits.MinFps || StreamFps > MediaLimits.MaxFps)
        {
            StreamFps = DefaultStreamFps;
            changed = true;
        }

        if (MaxFrameWidth < MediaLimits.MinWidth || MaxFrameWidth > MediaLimits.MaxWidth)
        {
            MaxFrameWidth = DefaultMaxFrameWidth;
            changed = true;
        }

        return changed;
    }

    private static bool IsValidPort(int port) => port is > 0 and <= 65535;
}
