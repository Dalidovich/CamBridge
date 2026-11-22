using CamBridge.Exceptions;
using CamBridge.Models;

namespace CamBridge.Services;

public sealed class StreamSubscription : IAsyncDisposable
{
    private readonly PhoneSession session;
    private readonly Guid subscriberId = Guid.NewGuid();
    private long lastSequence;
    private int disposed;

    private StreamSubscription(PhoneSession session)
    {
        this.session = session;
    }

    public static async Task<StreamSubscription> OpenAsync(PhoneSession session, StreamParameters parameters)
    {
        var subscription = new StreamSubscription(session);
        if (session.Frames.AddSubscriber(subscription.subscriberId, parameters, out var effective))
        {
            try
            {
                await session.ApplyStreamAsync(effective);
            }
            catch (PhoneNotConnectedException)
            {
                await subscription.DisposeAsync();
                throw;
            }
        }

        return subscription;
    }

    public async Task<byte[]?> WaitNextFrameAsync(CancellationToken cancellationToken)
    {
        var next = await session.Frames.WaitForFrameAfterAsync(lastSequence, cancellationToken);
        if (next is null)
        {
            return null;
        }

        lastSequence = next.Value.Sequence;
        return next.Value.Frame;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }

        if (!session.Frames.RemoveSubscriber(subscriberId, out var effective))
        {
            return;
        }

        try
        {
            await session.ApplyStreamAsync(effective);
        }
        catch (PhoneNotConnectedException)
        {
        }
    }
}
