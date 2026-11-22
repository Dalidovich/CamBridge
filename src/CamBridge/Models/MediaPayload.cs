namespace CamBridge.Models;

public sealed record MediaPayload(byte[] Data, string MimeType)
{
    public string FileExtension => MimeType.ToLowerInvariant() switch
    {
        var mime when mime.StartsWith("image/jpeg") => ".jpg",
        var mime when mime.StartsWith("video/mp4") => ".mp4",
        var mime when mime.StartsWith("video/quicktime") => ".mov",
        var mime when mime.StartsWith("video/webm") => ".webm",
        _ => ".bin"
    };
}
