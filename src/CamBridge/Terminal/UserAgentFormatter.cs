namespace CamBridge.Terminal;

public static class UserAgentFormatter
{
    private const int MaxLength = 60;

    private static readonly (string Token, string Name)[] Browsers =
    [
        ("EdgA/", "Edge"),
        ("EdgiOS/", "Edge"),
        ("SamsungBrowser/", "Samsung Internet"),
        ("OPR/", "Opera"),
        ("CriOS/", "Chrome"),
        ("FxiOS/", "Firefox"),
        ("Firefox/", "Firefox"),
        ("Chrome/", "Chrome"),
        ("Safari/", "Safari")
    ];

    private static readonly string[] NoiseParts = ["Linux", "U", "K", "Mobile", "wv"];

    public static string Shorten(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return "unknown device";
        }

        var platform = ExtractPlatform(userAgent);
        var browser = Browsers.FirstOrDefault(entry => userAgent.Contains(entry.Token, StringComparison.Ordinal)).Name;
        var result = string.Join(", ", new[] { platform, browser }.Where(part => !string.IsNullOrEmpty(part)));
        if (result.Length == 0)
        {
            result = userAgent;
        }

        return result.Length <= MaxLength ? result : result[..MaxLength];
    }

    private static string? ExtractPlatform(string userAgent)
    {
        var start = userAgent.IndexOf('(');
        var end = start < 0 ? -1 : userAgent.IndexOf(')', start + 1);
        if (start < 0 || end < 0)
        {
            return null;
        }

        var parts = userAgent[(start + 1)..end]
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !NoiseParts.Contains(part, StringComparer.OrdinalIgnoreCase))
            .Take(2)
            .Select(part => part.Replace("CPU iPhone OS ", "iOS ").Replace(" like Mac OS X", string.Empty).Replace('_', '.'));
        return string.Join(" ", parts);
    }
}
