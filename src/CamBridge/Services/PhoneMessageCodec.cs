using System.Buffers.Binary;
using System.Text.Json;
using CamBridge.Models;

namespace CamBridge.Services;

public static class PhoneMessageCodec
{
    public const string StreamFrameId = "stream";
    public const string LockFrameId = "lock";
    private const int HeaderLengthSize = 4;

    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web);

    public static byte[] Serialize(object message) => JsonSerializer.SerializeToUtf8Bytes(message, SerializerOptions);

    public static bool TryParseBinary(ReadOnlySpan<byte> frame, out string id, out PhoneReply reply)
    {
        id = string.Empty;
        reply = new PhoneReply(false, "Malformed binary frame.", null);

        if (frame.Length < HeaderLengthSize)
        {
            return false;
        }

        var headerLength = BinaryPrimitives.ReadUInt32BigEndian(frame);
        if (headerLength == 0 || headerLength > frame.Length - HeaderLengthSize)
        {
            return false;
        }

        try
        {
            using var header = JsonDocument.Parse(frame.Slice(HeaderLengthSize, (int)headerLength).ToArray());
            var root = header.RootElement;
            id = ReadString(root, "id") ?? string.Empty;
            if (id.Length == 0)
            {
                return false;
            }

            var ok = !root.TryGetProperty("ok", out var okElement) || okElement.ValueKind != JsonValueKind.False;
            var mime = ReadString(root, "mime") ?? "application/octet-stream";
            var payload = frame[(HeaderLengthSize + (int)headerLength)..].ToArray();
            reply = ok
                ? new PhoneReply(true, null, new MediaPayload(payload, mime))
                : new PhoneReply(false, ReadString(root, "error") ?? "The phone reported an error.", null);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string? ReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    public static PhoneReply ParseResult(JsonElement root)
    {
        var ok = root.TryGetProperty("ok", out var okElement) && okElement.ValueKind == JsonValueKind.True;
        return new PhoneReply(ok, ok ? null : ReadString(root, "error") ?? "The phone reported an error.", null);
    }

    public static PhoneCapabilities ParseCapabilities(JsonElement root)
    {
        if (!root.TryGetProperty("capabilities", out var capabilities) || capabilities.ValueKind != JsonValueKind.Object)
        {
            return new PhoneCapabilities(false, null, 0, 0, []);
        }

        var torch = capabilities.TryGetProperty("torch", out var torchElement) && torchElement.ValueKind == JsonValueKind.True;
        return new PhoneCapabilities(
            torch,
            ParseZoom(capabilities),
            ReadInt(capabilities, "width"),
            ReadInt(capabilities, "height"),
            ParseMimeTypes(capabilities));
    }

    private static ZoomCapability? ParseZoom(JsonElement capabilities)
    {
        if (!capabilities.TryGetProperty("zoom", out var zoom) || zoom.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var min = ReadDouble(zoom, "min");
        var max = ReadDouble(zoom, "max");
        if (min is null || max is null || max < min)
        {
            return null;
        }

        return new ZoomCapability(min.Value, max.Value, ReadDouble(zoom, "step") ?? 0.1);
    }

    private static List<string> ParseMimeTypes(JsonElement capabilities)
    {
        if (!capabilities.TryGetProperty("recordMimeTypes", out var types) || types.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return types.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToList();
    }

    private static int ReadInt(JsonElement element, string propertyName)
    {
        var value = ReadDouble(element, propertyName);
        return value is null ? 0 : (int)Math.Round(value.Value);
    }

    private static double? ReadDouble(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number)
                ? number
                : null;
    }
}
