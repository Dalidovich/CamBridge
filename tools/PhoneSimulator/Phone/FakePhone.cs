using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace PhoneSimulator.Phone;

public sealed class FakePhone : IAsyncDisposable
{
    private static readonly byte[] TinyJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAAMCAgICAgMCAgIDAwMDBAYEBAQEBAgGBgUGCQgKCgkICQkKDA8MCgsOCwkJDRENDg8QEBEQCgwSExIQEw8QEBD/yQALCAABAAEBAREA/8wABgAQEAX/2gAIAQEAAD8A0s8g/9k=");

    private readonly ClientWebSocket socket;
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private Task? receiveTask;
    private CancellationTokenSource? streamCancellation;

    private FakePhone(ClientWebSocket socket)
    {
        this.socket = socket;
    }

    public bool SendPings { get; set; } = true;

    public int SnapshotsServed { get; private set; }

    public int StreamFramesSent { get; private set; }

    public bool StreamEnabled { get; private set; }

    public string? CloseReason => socket.CloseStatusDescription;

    public Task Completion => receiveTask ?? Task.CompletedTask;

    public static async Task<(FakePhone? Phone, int StatusCode)> ConnectAsync(Uri uri)
    {
        var socket = new ClientWebSocket();
        socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.SetRequestHeader("User-Agent", "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Mobile Safari/537.36");
        try
        {
            await socket.ConnectAsync(uri, CancellationToken.None);
        }
        catch (WebSocketException)
        {
            var status = (int)socket.HttpStatusCode;
            socket.Dispose();
            return (null, status);
        }

        var phone = new FakePhone(socket);
        phone.Start();
        return (phone, (int)socket.HttpStatusCode);
    }

    public async Task CloseAsync()
    {
        if (socket.State == WebSocketState.Open)
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnected on the phone", CancellationToken.None);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        streamCancellation?.Cancel();
        socket.Abort();
        socket.Dispose();
    }

    private void Start()
    {
        receiveTask = Task.Run(ReceiveLoopAsync);
        _ = Task.Run(PingLoopAsync);
        _ = SendJsonAsync(new
        {
            type = "hello",
            userAgent = "PhoneSimulator",
            capabilities = new
            {
                torch = true,
                zoom = new { min = 1.0, max = 8.0, step = 0.1 },
                width = 1920,
                height = 1080,
                recordMimeTypes = new[] { "video/webm" }
            }
        });
    }

    private async Task PingLoopAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), lifetime.Token);
                if (SendPings)
                {
                    await SendJsonAsync(new { type = "ping" });
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
        }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, lifetime.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        if (socket.State == WebSocketState.CloseReceived)
                        {
                            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Bye", CancellationToken.None);
                        }

                        return;
                    }

                    message.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    await HandleAsync(Encoding.UTF8.GetString(message.ToArray()));
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
        }
        finally
        {
            streamCancellation?.Cancel();
        }
    }

    private async Task HandleAsync(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var type = root.GetProperty("type").GetString();
        var id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
        switch (type)
        {
            case "snapshot":
                SnapshotsServed++;
                await SendBinaryAsync(id!, "image/jpeg", TinyJpeg);
                break;
            case "torch":
            case "zoom":
                await SendJsonAsync(new { type = "result", id, ok = true });
                break;
            case "record":
                var seconds = root.GetProperty("seconds").GetInt32();
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(seconds, 2)));
                    await SendBinaryAsync(id!, "video/webm", Encoding.ASCII.GetBytes(new string('V', 4096)));
                });
                break;
            case "stream":
                HandleStream(root);
                break;
        }
    }

    private void HandleStream(JsonElement root)
    {
        streamCancellation?.Cancel();
        StreamEnabled = root.GetProperty("enabled").GetBoolean();
        if (!StreamEnabled)
        {
            return;
        }

        var fps = root.GetProperty("fps").GetInt32();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        streamCancellation = cancellation;
        _ = Task.Run(async () =>
        {
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    await SendBinaryAsync("stream", "image/jpeg", TinyJpeg);
                    StreamFramesSent++;
                    await Task.Delay(TimeSpan.FromMilliseconds(1000.0 / fps), cancellation.Token);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
            {
            }
        });
    }

    private async Task SendJsonAsync(object message)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        await SendAsync(bytes, WebSocketMessageType.Text);
    }

    private async Task SendBinaryAsync(string id, string mime, byte[] payload)
    {
        var header = JsonSerializer.SerializeToUtf8Bytes(new { id, mime, ok = true });
        var frame = new byte[4 + header.Length + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)header.Length);
        header.CopyTo(frame, 4);
        payload.CopyTo(frame, 4 + header.Length);
        await SendAsync(frame, WebSocketMessageType.Binary);
    }

    private async Task SendAsync(byte[] bytes, WebSocketMessageType type)
    {
        await sendLock.WaitAsync();
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(bytes, type, true, CancellationToken.None);
            }
        }
        finally
        {
            sendLock.Release();
        }
    }
}
