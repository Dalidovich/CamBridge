using CamBridge.Models;

namespace CamBridge.Services;

public sealed class FrameStream
{
    private readonly object sync = new();
    private readonly Dictionary<Guid, StreamParameters> subscribers = [];
    private TaskCompletionSource frameSignal = NewSignal();
    private byte[]? latestFrame;
    private long latestSequence;
    private DateTimeOffset latestFrameAt;
    private bool completed;

    public StreamParameters? Effective
    {
        get
        {
            lock (sync)
            {
                return ComputeEffective();
            }
        }
    }

    public bool IsActive
    {
        get
        {
            lock (sync)
            {
                return subscribers.Count > 0;
            }
        }
    }

    public bool AddSubscriber(Guid subscriberId, StreamParameters parameters, out StreamParameters? effective)
    {
        lock (sync)
        {
            var before = ComputeEffective();
            subscribers[subscriberId] = parameters;
            effective = ComputeEffective();
            return before != effective;
        }
    }

    public bool RemoveSubscriber(Guid subscriberId, out StreamParameters? effective)
    {
        lock (sync)
        {
            var before = ComputeEffective();
            subscribers.Remove(subscriberId);
            effective = ComputeEffective();
            if (effective is null)
            {
                latestFrame = null;
            }

            return before != effective;
        }
    }

    public void Publish(byte[] frame)
    {
        TaskCompletionSource signal;
        lock (sync)
        {
            if (completed || subscribers.Count == 0)
            {
                return;
            }

            latestFrame = frame;
            latestSequence++;
            latestFrameAt = DateTimeOffset.UtcNow;
            signal = frameSignal;
            frameSignal = NewSignal();
        }

        signal.TrySetResult();
    }

    public byte[]? TryGetFreshFrame(TimeSpan maxAge)
    {
        lock (sync)
        {
            return latestFrame is not null && subscribers.Count > 0 && DateTimeOffset.UtcNow - latestFrameAt <= maxAge
                ? latestFrame
                : null;
        }
    }

    public async Task<(byte[] Frame, long Sequence)?> WaitForFrameAfterAsync(long sequence, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task waitTask;
            lock (sync)
            {
                if (completed)
                {
                    return null;
                }

                if (latestFrame is not null && latestSequence > sequence)
                {
                    return (latestFrame, latestSequence);
                }

                waitTask = frameSignal.Task;
            }

            await waitTask.WaitAsync(cancellationToken);
        }
    }

    public void Complete()
    {
        TaskCompletionSource signal;
        lock (sync)
        {
            completed = true;
            latestFrame = null;
            signal = frameSignal;
        }

        signal.TrySetResult();
    }

    private StreamParameters? ComputeEffective()
    {
        if (subscribers.Count == 0)
        {
            return null;
        }

        var values = subscribers.Values;
        return new StreamParameters(values.Max(item => item.Fps), values.Max(item => item.Quality), values.Max(item => item.MaxWidth));
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
