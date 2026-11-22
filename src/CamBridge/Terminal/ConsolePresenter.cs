using CamBridge.Configuration;
using CamBridge.Infrastructure;
using CamBridge.Services;

namespace CamBridge.Terminal;

public sealed class ConsolePresenter
{
    private readonly object outputLock = new();
    private readonly AppSettings settings;
    private readonly NetworkSelection network;
    private readonly PairingTokenService tokens;

    public ConsolePresenter(AppSettings settings, NetworkSelection network, PairingTokenService tokens, PhoneSessionManager sessions)
    {
        this.settings = settings;
        this.network = network;
        this.tokens = tokens;
        sessions.SessionStarted += OnSessionStarted;
        sessions.SessionEnded += OnSessionEnded;
    }

    public string ApiBaseUrl => $"http://127.0.0.1:{settings.ApiPort}";

    public void PrintStartup()
    {
        lock (outputLock)
        {
            WriteColored("CamBridge — phone camera bridge", ConsoleColor.Cyan);
            Console.WriteLine();
            WritePairingBlock(tokens.CurrentToken);
            Console.WriteLine($"MCP endpoint:  {ApiBaseUrl}/mcp");
            Console.WriteLine($"REST base:     {ApiBaseUrl}/api");
            Console.WriteLine();
            Console.WriteLine("The phone browser will warn once about the self-signed certificate; accept it to continue.");
            Console.WriteLine("Keys: D = disconnect phone, Q = quit (Ctrl+C also quits).");
            Console.WriteLine();
        }
    }

    public void PrintError(string message)
    {
        lock (outputLock)
        {
            WriteColored(message, ConsoleColor.Red);
        }
    }

    public void PrintInfo(string message)
    {
        lock (outputLock)
        {
            Console.WriteLine(message);
        }
    }

    private void OnSessionStarted(PhoneSession session)
    {
        lock (outputLock)
        {
            WriteColored($"Phone connected ({UserAgentFormatter.Shorten(session.UserAgent)}, {session.Ip})", ConsoleColor.Green);
        }
    }

    private void OnSessionEnded(string newToken)
    {
        lock (outputLock)
        {
            WriteColored("Phone disconnected — new QR:", ConsoleColor.Yellow);
            Console.WriteLine();
            WritePairingBlock(newToken);
        }
    }

    private void WritePairingBlock(string token)
    {
        var url = BuildPairingUrl(network.PrimaryAddress.ToString(), token);
        WriteQr(url);
        Console.WriteLine();
        Console.WriteLine($"Scan with your phone or open: {url}");
        foreach (var address in network.OtherAddresses)
        {
            Console.WriteLine($"  other address:              {BuildPairingUrl(address.ToString(), token)}");
        }

        Console.WriteLine();
    }

    private string BuildPairingUrl(string host, string token) => $"https://{host}:{settings.PhonePort}/?t={token}";

    private static void WriteQr(string content)
    {
        foreach (var line in QrConsoleRenderer.Render(content))
        {
            Console.Write("  ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.BackgroundColor = ConsoleColor.Black;
            Console.Write(line);
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    private static void WriteColored(string message, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.Write(message);
        Console.ResetColor();
        Console.WriteLine();
    }
}
