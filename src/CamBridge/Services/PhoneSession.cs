using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using CamBridge.Exceptions;
using CamBridge.Models;

namespace CamBridge.Services;

public sealed class PhoneSession
{
    public const int MaxMessageBytes = 64 * 1024 * 1024;
    private const int ReceiveBufferBytes = 64 * 1024;
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CloseGracePeriod = TimeSpan.FromSeconds(2);

    private readonly WebSocket socket;
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<PhoneReply>> pending = new();
    private readonly CancellationTokenSource lifetime = new();
    private int closeRequested;

    public PhoneSession(WebSocket socket, string ip, string userAgent)
    {
        this.socket = socket;
        Ip = ip;
        UserAgent = userAgent;
        ConnectedAt = DateTimeOffset.Now;
    }

    public Guid Id { get; } = Guid.NewGuid();

    public string Ip { get; }

    public string UserAgent { get; }

    public DateTimeOffset ConnectedAt { get; }

    public PhoneCapabilities? Capabilities { get; private set; }

    public bool TorchOn { get; set; }

    public double? ZoomValue { get; set; }

    public FrameStream Frames { get; } = new();

    public async Task RunAsync()
    {
        try
        {
            await ReceiveLoopAsync();
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
        }
        finally
        {
            Frames.Complete();
            FailPendingRequests();
            if (socket.State != WebSocketState.Closed)
            {
                socket.Abort();
            }
        }
    }

    public async Task CloseAsync(string reason)
    {
        if (Interlocked.Exchange(ref closeRequested, 1) == 1)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(CloseGracePeriod);
            await sendLock.WaitAsync(timeout.Token);
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, reason, timeout.Token);
                }
            }
            finally
            {
                sendLock.Release();
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
        }
        finally
        {
            CancelAfterGracePeriod();
        }
    }

    public async Task SendAsync(object message, CancellationToken cancellationToken)
    {
        var bytes = PhoneMessageCodec.Serialize(message);
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
            await sendLock.WaitAsync(linked.Token);
            try
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, linked.Token);
            }
            finally
            {
                sendLock.Release();
            }
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new PhoneNotConnectedException("The phone disconnected.");
        }
    }

    public async Task<PhoneReply> RequestAsync(string operation, Func<string, object> createMessage, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString();
        var completion = new TaskCompletionSource<PhoneReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        try
        {
            if (lifetime.IsCancellationRequested)
            {
                throw new PhoneNotConnectedException();
            }

            await SendAsync(createMessage(id), cancellationToken);
            return await completion.Task.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            throw new PhoneTimeoutException(operation, timeout);
        }
        finally
        {
            pending.TryRemove(id, out _);
        }
    }

    public async Task ApplyStreamAsync(StreamParameters? parameters)
    {
        object message = parameters is null
            ? new { type = "stream", enabled = false }
            : new { type = "stream", enabled = true, fps = parameters.Fps, quality = parameters.Quality, maxWidth = parameters.MaxWidth };
        await SendAsync(message, CancellationToken.None);
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[ReceiveBufferBytes];
        using var message = new MemoryStream();
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);

        while (true)
        {
            message.SetLength(0);
            ValueWebSocketReceiveResult result;
            do
            {
                idle.CancelAfter(IdleTimeout);
                result = await socket.ReceiveAsync(buffer.AsMemory(), idle.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await AcknowledgeCloseAsync();
                    return;
                }

                if (message.Length + result.Count > MaxMessageBytes)
                {
                    await CloseAsync("Message too large");
                    return;
                }

                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            var data = new ReadOnlyMemory<byte>(message.GetBuffer(), 0, (int)message.Length);
            if (result.MessageType == WebSocketMessageType.Binary)
            {
                HandleBinary(data.Span);
            }
            else
            {
                await HandleTextAsync(data);
            }
        }
    }

    private void HandleBinary(ReadOnlySpan<byte> data)
    {
        if (!PhoneMessageCodec.TryParseBinary(data, out var id, out var reply))
        {
            return;
        }

        if (id == PhoneMessageCodec.StreamFrameId)
        {
            if (reply.Media is not null)
            {
                Frames.Publish(reply.Media.Data);
            }

            return;
        }

        if (id == PhoneMessageCodec.LockFrameId)
        {
            if (reply.Media is not null)
            {
                Frames.Lock(reply.Media.Data);
            }

            return;
        }

        Complete(id, reply);
    }

    private async Task HandleTextAsync(ReadOnlyMemory<byte> data)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(data);
        }
        catch (JsonException)
        {
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            switch (PhoneMessageCodec.ReadString(root, "type"))
            {
                case "ping":
                    await SendAsync(new { type = "pong" }, CancellationToken.None);
                    break;
                case "hello":
                    Capabilities = PhoneMessageCodec.ParseCapabilities(root);
                    var effective = Frames.Effective;
                    if (effective is not null)
                    {
                        await ApplyStreamAsync(effective);
                    }

                    break;
                case "unlock":
                    Frames.Unlock();
                    break;
                case "result":
                    var id = PhoneMessageCodec.ReadString(root, "id");
                    if (id is not null)
                    {
                        Complete(id, PhoneMessageCodec.ParseResult(root));
                    }

                    break;
            }
        }
    }

    private void Complete(string id, PhoneReply reply)
    {
        if (pending.TryRemove(id, out var completion))
        {
            completion.TrySetResult(reply);
        }
    }

    private async Task AcknowledgeCloseAsync()
    {
        if (socket.State != WebSocketState.CloseReceived)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(CloseGracePeriod);
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Bye", timeout.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
        }
    }

    private void CancelAfterGracePeriod()
    {
        try
        {
            lifetime.CancelAfter(CloseGracePeriod);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void FailPendingRequests()
    {
        foreach (var id in pending.Keys)
        {
            if (pending.TryRemove(id, out var completion))
            {
                completion.TrySetException(new PhoneNotConnectedException("The phone disconnected before answering."));
            }
        }
    }
}
