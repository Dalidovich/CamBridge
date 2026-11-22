using CamBridge.Services;

namespace CamBridge.Terminal;

public sealed class KeyboardListener : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly PhoneSessionManager sessions;
    private readonly IHostApplicationLifetime lifetime;
    private readonly ConsolePresenter presenter;

    public KeyboardListener(PhoneSessionManager sessions, IHostApplicationLifetime lifetime, ConsolePresenter presenter)
    {
        this.sessions = sessions;
        this.lifetime = lifetime;
        this.presenter = presenter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (Console.IsInputRedirected)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var key = TryReadKey();
            if (key is null)
            {
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
                continue;
            }

            await HandleKeyAsync(key.Value);
        }
    }

    private async Task HandleKeyAsync(ConsoleKey key)
    {
        switch (key)
        {
            case ConsoleKey.D:
                if (sessions.Current is null)
                {
                    presenter.PrintInfo("No phone is connected.");
                    return;
                }

                presenter.PrintInfo("Disconnecting the phone...");
                await sessions.KickAsync();
                break;
            case ConsoleKey.Q:
                presenter.PrintInfo("Shutting down...");
                lifetime.StopApplication();
                break;
        }
    }

    private static ConsoleKey? TryReadKey()
    {
        try
        {
            return Console.KeyAvailable ? Console.ReadKey(intercept: true).Key : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
