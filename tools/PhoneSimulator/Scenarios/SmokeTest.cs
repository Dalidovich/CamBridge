using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PhoneSimulator.Http;
using PhoneSimulator.Phone;

namespace PhoneSimulator.Scenarios;

public sealed partial class SmokeTest
{
    private const string NoPhoneMessage = "No phone connected. Scan the QR code shown in the CamBridge console.";

    private readonly string consoleLogPath;
    private readonly int phonePort;
    private readonly HttpClient phoneHttp;
    private readonly HttpClient apiHttp;
    private readonly McpClient mcp;
    private int failures;

    public SmokeTest(string consoleLogPath, int phonePort, int apiPort)
    {
        this.consoleLogPath = consoleLogPath;
        this.phonePort = phonePort;
        phoneHttp = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true })
        {
            BaseAddress = new Uri($"https://127.0.0.1:{phonePort}/")
        };
        apiHttp = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{apiPort}/"), Timeout = TimeSpan.FromSeconds(60) };
        mcp = new McpClient(apiHttp, new Uri($"http://127.0.0.1:{apiPort}/mcp"));
    }

    public async Task<bool> RunAsync()
    {
        await CheckPhonePageAsync();
        await CheckDisconnectedApiAsync();
        await CheckMcpWithoutPhoneAsync();
        await CheckWrongTokenAsync();

        var token = ReadLatestToken();
        var (phone, status) = await FakePhone.ConnectAsync(WsUri(token));
        Check("WS with current token accepted (101)", phone is not null, $"status {status}");
        if (phone is null)
        {
            return false;
        }

        await using (phone)
        {
            await Task.Delay(500);
            var (second, secondStatus) = await FakePhone.ConnectAsync(WsUri(token));
            Check("Second WS while paired rejected with 409", second is null && secondStatus == 409, $"status {secondStatus}");
            await CheckConnectedApiAsync(phone);
            await CheckConnectedMcpAsync();

            await phone.CloseAsync();
            await phone.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }

        await Task.Delay(1000);
        var afterClose = await apiHttp.GetFromJsonAsync<JsonObject>("api/status");
        Check("Status disconnected after phone closed", afterClose?["connected"]?.GetValue<bool>() == false, afterClose?.ToJsonString());
        var (stale, staleStatus) = await FakePhone.ConnectAsync(WsUri(token));
        Check("Old token rejected after disconnect (403)", stale is null && staleStatus == 403, $"status {staleStatus}");
        var rotated = ReadLatestToken();
        Check("Token rotated after disconnect", rotated != token, $"{token} -> {rotated}");

        await CheckIdleTimeoutAsync(rotated);

        Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        return failures == 0;
    }

    private async Task CheckPhonePageAsync()
    {
        using var response = await phoneHttp.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();
        Check("GET https://127.0.0.1/ returns the phone page over TLS", response.IsSuccessStatusCode && body.Contains("CamBridge"), $"{(int)response.StatusCode}, {body.Length} chars");
        using var script = await phoneHttp.GetAsync("/app.js");
        Check("GET /app.js served from embedded resources", script.IsSuccessStatusCode, $"{(int)script.StatusCode} {script.Content.Headers.ContentType}");
        using var apiOnPhonePort = await phoneHttp.GetAsync("/api/status");
        Check("REST is not exposed on the phone port", (int)apiOnPhonePort.StatusCode == 404, $"{(int)apiOnPhonePort.StatusCode}");
    }

    private async Task CheckDisconnectedApiAsync()
    {
        var status = await apiHttp.GetFromJsonAsync<JsonObject>("api/status");
        Check("GET /api/status connected:false", status?["connected"]?.GetValue<bool>() == false, status?.ToJsonString());
        using var snapshot = await apiHttp.GetAsync("api/snapshot");
        Check("GET /api/snapshot without phone -> 503", (int)snapshot.StatusCode == 503, await snapshot.Content.ReadAsStringAsync());
    }

    private async Task CheckMcpWithoutPhoneAsync()
    {
        var initialize = await mcp.InitializeAsync();
        Check("MCP initialize", initialize?["serverInfo"]?["name"]?.GetValue<string>() == "CamBridge", initialize?["serverInfo"]?.ToJsonString());
        var tools = await mcp.ListToolsAsync();
        var names = tools?["tools"]?.AsArray().Select(tool => tool?["name"]?.GetValue<string>()).OrderBy(name => name).ToList() ?? [];
        Check("MCP tools/list returns 6 tools", names.Count == 6, string.Join(", ", names));
        var snapshot = await mcp.CallToolAsync("take_snapshot", new JsonObject());
        var text = snapshot?["content"]?[0]?["text"]?.GetValue<string>();
        Check("MCP take_snapshot without phone -> isError", snapshot?["isError"]?.GetValue<bool>() == true && text == NoPhoneMessage, text);
    }

    private async Task CheckWrongTokenAsync()
    {
        var (phone, status) = await FakePhone.ConnectAsync(WsUri("wrong-token"));
        Check("WS with wrong token -> 403", phone is null && status == 403, $"status {status}");
        using var check = await phoneHttp.GetAsync("/ws/status?t=wrong-token");
        Check("GET /ws/status wrong token -> 403", (int)check.StatusCode == 403, $"{(int)check.StatusCode}");
    }

    private async Task CheckConnectedApiAsync(FakePhone phone)
    {
        var status = await apiHttp.GetFromJsonAsync<JsonObject>("api/status");
        Check("GET /api/status connected:true with capabilities", status?["connected"]?.GetValue<bool>() == true && status?["capabilities"] is not null, status?.ToJsonString());

        using var snapshot = await apiHttp.GetAsync("api/snapshot?quality=0.5&maxWidth=640");
        var bytes = await snapshot.Content.ReadAsByteArrayAsync();
        Check("GET /api/snapshot -> image/jpeg", snapshot.IsSuccessStatusCode && snapshot.Content.Headers.ContentType?.MediaType == "image/jpeg" && bytes.Length > 0, $"{(int)snapshot.StatusCode}, {bytes.Length} bytes, served {phone.SnapshotsServed}");

        using var torch = await apiHttp.PostAsJsonAsync("api/torch", new { on = true });
        Check("POST /api/torch", torch.IsSuccessStatusCode, await torch.Content.ReadAsStringAsync());

        using var zoom = await apiHttp.PostAsJsonAsync("api/zoom", new { value = 20 });
        Check("POST /api/zoom clamps to range", zoom.IsSuccessStatusCode, await zoom.Content.ReadAsStringAsync());

        using var clip = await apiHttp.GetAsync("api/clip?seconds=1");
        var clipBytes = await clip.Content.ReadAsByteArrayAsync();
        Check("GET /api/clip -> video attachment", clip.IsSuccessStatusCode && clip.Content.Headers.ContentDisposition?.DispositionType == "attachment", $"{(int)clip.StatusCode}, {clip.Content.Headers.ContentType}, {clipBytes.Length} bytes, {clip.Content.Headers.ContentDisposition}");

        await CheckMjpegAsync(phone);
    }

    private async Task CheckMjpegAsync(FakePhone phone)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/stream?fps=10&quality=0.5&maxWidth=320");
        using var response = await apiHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var contentType = response.Content.Headers.ContentType?.ToString();
        await using var stream = await response.Content.ReadAsStreamAsync();
        var buffer = new byte[8192];
        var received = new MemoryStream();
        using var window = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer, window.Token);
                if (read == 0)
                {
                    break;
                }

                received.Write(buffer, 0, read);
            }
        }
        catch (OperationCanceledException)
        {
        }

        var text = System.Text.Encoding.ASCII.GetString(received.ToArray());
        var frames = Regex.Matches(text, "--frame\r\n").Count;
        Check("GET /api/stream MJPEG frames", frames >= 5 && contentType?.Contains("multipart/x-mixed-replace") == true, $"{frames} frames in 2 s, {contentType}, phone streaming={phone.StreamEnabled}");
        response.Dispose();
        await Task.Delay(1000);
        Check("Stream disabled after last MJPEG client left", !phone.StreamEnabled, $"phone streaming={phone.StreamEnabled}");
    }

    private async Task CheckConnectedMcpAsync()
    {
        var status = await mcp.CallToolAsync("get_status", new JsonObject());
        Check("MCP get_status connected", status?["content"]?[0]?["text"]?.GetValue<string>()?.Contains("\"connected\": true") == true, status?["content"]?[0]?["text"]?.GetValue<string>()?.Split('\n').Length + " lines");

        var snapshot = await mcp.CallToolAsync("take_snapshot", new JsonObject { ["quality"] = 0.6, ["max_width"] = 800 });
        Check("MCP take_snapshot -> image", snapshot?["content"]?[0]?["type"]?.GetValue<string>() == "image" && snapshot?["content"]?[0]?["mimeType"]?.GetValue<string>() == "image/jpeg", Summarize(snapshot));

        var frames = await mcp.CallToolAsync("capture_frames", new JsonObject { ["count"] = 3, ["interval_ms"] = 100 });
        Check("MCP capture_frames -> 3 images", frames?["content"]?.AsArray().Count == 3, Summarize(frames));

        var torch = await mcp.CallToolAsync("set_torch", new JsonObject { ["on"] = false });
        Check("MCP set_torch", torch?["isError"]?.GetValue<bool>() != true, torch?["content"]?[0]?["text"]?.GetValue<string>());

        var zoom = await mcp.CallToolAsync("set_zoom", new JsonObject { ["value"] = 0.2 });
        Check("MCP set_zoom", zoom?["isError"]?.GetValue<bool>() != true, zoom?["content"]?[0]?["text"]?.GetValue<string>());

        var clip = await mcp.CallToolAsync("record_clip", new JsonObject { ["seconds"] = 1 });
        Check("MCP record_clip -> resource + text", clip?["content"]?[0]?["type"]?.GetValue<string>() == "resource" && clip?["content"]?[1]?["type"]?.GetValue<string>() == "text", Summarize(clip));
    }

    private async Task CheckIdleTimeoutAsync(string token)
    {
        var (phone, status) = await FakePhone.ConnectAsync(WsUri(token));
        if (phone is null)
        {
            Check("Idle timeout phone connected", false, $"status {status}");
            return;
        }

        await using (phone)
        {
            phone.SendPings = false;
            var started = DateTime.UtcNow;
            var finished = await Task.WhenAny(phone.Completion, Task.Delay(TimeSpan.FromSeconds(20))) == phone.Completion;
            var elapsed = DateTime.UtcNow - started;
            Check("Session dropped after ~10 s without heartbeat", finished && elapsed.TotalSeconds is > 8 and < 16, $"{elapsed.TotalSeconds:0.0} s");
        }

        await Task.Delay(500);
        Check("Token rotated after idle timeout", ReadLatestToken() != token, ReadLatestToken());
    }

    private static string Summarize(JsonNode? result)
    {
        var content = result?["content"]?.AsArray();
        if (content is null)
        {
            return result?.ToJsonString() ?? "null";
        }

        return string.Join("; ", content.Select(item => item?["type"]?.GetValue<string>() switch
        {
            "image" => $"image {item?["mimeType"]} {item?["data"]?.GetValue<string>().Length} b64 chars",
            "resource" => $"resource {item?["resource"]?["mimeType"]} {item?["resource"]?["uri"]} {item?["resource"]?["blob"]?.GetValue<string>().Length} b64 chars",
            "text" => $"text '{item?["text"]}'",
            var other => other ?? "unknown"
        })) + (result?["isError"]?.GetValue<bool>() == true ? " (isError)" : string.Empty);
    }

    private Uri WsUri(string token) => new($"wss://127.0.0.1:{phonePort}/ws?t={Uri.EscapeDataString(token)}");

    private string ReadLatestToken()
    {
        using var stream = new FileStream(consoleLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var matches = TokenPattern().Matches(reader.ReadToEnd());
        return matches.Count == 0 ? string.Empty : matches[^1].Groups[1].Value;
    }

    private void Check(string name, bool passed, string? detail)
    {
        if (!passed)
        {
            failures++;
        }

        Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {name} :: {detail}");
    }

    [GeneratedRegex(@"\?t=([A-Za-z0-9_\-]+)")]
    private static partial Regex TokenPattern();
}
