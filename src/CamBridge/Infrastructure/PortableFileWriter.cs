namespace CamBridge.Infrastructure;

public static class PortableFileWriter
{
    private static int readOnlyNoticeShown;

    public static bool TryWrite(string path, byte[] content)
    {
        try
        {
            File.WriteAllBytes(path, content);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            ReportReadOnlyFolder();
            return false;
        }
    }

    private static void ReportReadOnlyFolder()
    {
        if (Interlocked.Exchange(ref readOnlyNoticeShown, 1) == 0)
        {
            Console.Error.WriteLine($"Warning: cannot write to {PortablePaths.AppRoot}; settings and certificate are kept in memory only.");
        }
    }
}
