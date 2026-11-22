using System.Text.Json;
using CamBridge.Infrastructure;

namespace CamBridge.Configuration;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static AppSettings Load()
    {
        var path = PortablePaths.SettingsFile;
        var settings = TryRead(path);
        var mustWrite = settings is null;
        settings ??= new AppSettings();

        if (settings.Normalize())
        {
            mustWrite = true;
        }

        if (mustWrite)
        {
            Save(settings);
        }

        return settings;
    }

    private static AppSettings? TryRead(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Console.Error.WriteLine($"Warning: {Path.GetFileName(path)} is invalid and was reset to defaults.");
            return null;
        }
    }

    private static void Save(AppSettings settings)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(settings, SerializerOptions);
        PortableFileWriter.TryWrite(PortablePaths.SettingsFile, json);
    }
}
