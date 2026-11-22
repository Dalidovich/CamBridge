using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace CamBridge.Services;

public sealed class PairingTokenService
{
    private const int TokenSizeBytes = 16;
    private readonly object sync = new();
    private string currentToken = CreateToken();

    public string CurrentToken
    {
        get
        {
            lock (sync)
            {
                return currentToken;
            }
        }
    }

    public string Rotate()
    {
        lock (sync)
        {
            currentToken = CreateToken();
            return currentToken;
        }
    }

    public bool Matches(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(CurrentToken);
        var actual = Encoding.ASCII.GetBytes(candidate);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static string CreateToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenSizeBytes));
}
