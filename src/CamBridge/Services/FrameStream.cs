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
    private byte[]? lockedFrame;
    private DateTimeOffset? lockedAt;
    private bool completed;

    public byte[]? LockedFrame
    {
        get
        {
            lock (sync)
            {
                return lockedFrame;
            }
        }
    }

    public DateTimeOffset? LockedAt
    {
        get
        {
            lock (sync)
            {
                return lockedAt;
            }
        }
    }

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
            if (effective is null && lockedFrame is null)
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
            if (completed || subscribers.Count == 0 || lockedFrame is not null)
            {
                return;
            }

            signal = StoreLatest(frame);
        }

        signal.TrySetResult();
    }

    public void Lock(byte[] frame)
    {
        TaskCompletionSource signal;
        lock (sync)
        {
            if (completed)
            {
                return;
            }

            lockedFrame = frame;
            lockedAt = DateTimeOffset.Now;
            signal = StoreLatest(frame);
        }

        signal.TrySetResult();
    }

    public void Unlock()
    {
        lock (sync)
        {
            if (lockedFrame is null)
            {
                return;
            }

            lockedFrame = null;
            lockedAt = null;
            latestFrame = null;
        }
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
            lockedFrame = null;
            lockedAt = null;
            signal = frameSignal;
        }

        signal.TrySetResult();
    }

    private TaskCompletionSource StoreLatest(byte[] frame)
    {
        latestFrame = frame;
        latestSequence++;
        latestFrameAt = DateTimeOffset.UtcNow;
        var signal = frameSignal;
        frameSignal = NewSignal();
        return signal;
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
