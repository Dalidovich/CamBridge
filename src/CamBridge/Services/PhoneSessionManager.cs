using System.Net.WebSockets;
using CamBridge.Models;

namespace CamBridge.Services;

public sealed class PhoneSessionManager
{
    private readonly object sync = new();
    private readonly PairingTokenService tokens;
    private PhoneSession? current;
    private bool reserved;
    private bool stopping;

    public PhoneSessionManager(PairingTokenService tokens, IHostApplicationLifetime lifetime)
    {
        this.tokens = tokens;
        lifetime.ApplicationStopping.Register(OnApplicationStopping);
    }

    public event Action<PhoneSession>? SessionStarted;

    public event Action<string>? SessionEnded;

    public PhoneSession? Current
    {
        get
        {
            lock (sync)
            {
                return current;
            }
        }
    }

    public PairingCheckResult Check(string? token)
    {
        lock (sync)
        {
            return Evaluate(token);
        }
    }

    public PairingCheckResult TryReserve(string? token)
    {
        lock (sync)
        {
            var result = Evaluate(token);
            if (result == PairingCheckResult.Accepted)
            {
                reserved = true;
            }

            return result;
        }
    }

    public void ReleaseReservation()
    {
        lock (sync)
        {
            reserved = false;
        }
    }

    public async Task RunSessionAsync(WebSocket socket, string ip, string userAgent)
    {
        var session = new PhoneSession(socket, ip, userAgent);
        lock (sync)
        {
            current = session;
            reserved = false;
        }

        SessionStarted?.Invoke(session);
        try
        {
            await session.RunAsync();
        }
        finally
        {
            EndSession(session);
        }
    }

    public Task KickAsync()
    {
        var session = Current;
        return session is null ? Task.CompletedTask : session.CloseAsync("Disconnected from the PC");
    }

    private PairingCheckResult Evaluate(string? token)
    {
        if (stopping || !tokens.Matches(token))
        {
            return PairingCheckResult.InvalidToken;
        }

        return current is not null || reserved ? PairingCheckResult.Busy : PairingCheckResult.Accepted;
    }

    private void EndSession(PhoneSession session)
    {
        string newToken;
        lock (sync)
        {
            if (!ReferenceEquals(current, session))
            {
                return;
            }

            current = null;
            if (stopping)
            {
                return;
            }

            newToken = tokens.Rotate();
        }

        SessionEnded?.Invoke(newToken);
    }

    private void OnApplicationStopping()
    {
        PhoneSession? session;
        lock (sync)
        {
            stopping = true;
            session = current;
        }

        session?.CloseAsync("CamBridge was closed on the PC").GetAwaiter().GetResult();
    }
}
