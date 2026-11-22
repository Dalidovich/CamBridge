using System.Diagnostics;

namespace CamBridge.Infrastructure;

public static class PortablePaths
{
    private static readonly Lazy<string> AppRootLazy = new(ResolveAppRoot);

    public static string AppRoot => AppRootLazy.Value;

    public static string SettingsFile => Path.Combine(AppRoot, "settings.json");

    public static string CertificateFile => Path.Combine(AppRoot, "cert.pfx");

    private static string ResolveAppRoot()
    {
        return DirectoryOf(Environment.ProcessPath)
            ?? DirectoryOf(TryGetMainModulePath())
            ?? Directory.GetCurrentDirectory();
    }

    private static string? TryGetMainModulePath()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return process.MainModule?.FileName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? DirectoryOf(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        return string.IsNullOrEmpty(directory) ? null : directory;
    }
}
