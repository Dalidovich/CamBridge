using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PhoneSimulator.Http;

public sealed class McpClient
{
    private readonly HttpClient http;
    private readonly Uri endpoint;
    private int nextId;
    private string? sessionId;

    public McpClient(HttpClient http, Uri endpoint)
    {
        this.http = http;
        this.endpoint = endpoint;
    }

    public async Task<JsonNode?> InitializeAsync()
    {
        var result = await CallAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = "2025-06-18",
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "PhoneSimulator", ["version"] = "1.0.0" }
        });
        await NotifyAsync("notifications/initialized");
        return result;
    }

    public Task<JsonNode?> ListToolsAsync() => CallAsync("tools/list", new JsonObject());

    public Task<JsonNode?> CallToolAsync(string name, JsonObject arguments) =>
        CallAsync("tools/call", new JsonObject { ["name"] = name, ["arguments"] = arguments });

    private async Task<JsonNode?> CallAsync(string method, JsonObject parameters)
    {
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = Interlocked.Increment(ref nextId),
            ["method"] = method,
            ["params"] = parameters
        };
        using var response = await SendAsync(request);
        response.EnsureSuccessStatusCode();
        if (response.Headers.TryGetValues("Mcp-Session-Id", out var values))
        {
            sessionId = values.First();
        }

        var body = await response.Content.ReadAsStringAsync();
        var json = response.Content.Headers.ContentType?.MediaType == "text/event-stream" ? ExtractSseData(body) : body;
        var node = JsonNode.Parse(json);
        if (node?["error"] is { } error)
        {
            throw new InvalidOperationException($"MCP error for {method}: {error.ToJsonString()}");
        }

        return node?["result"];
    }

    private async Task NotifyAsync(string method)
    {
        using var response = await SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method });
    }

    private Task<HttpResponseMessage> SendAsync(JsonObject payload)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (sessionId is not null)
        {
            message.Headers.Add("Mcp-Session-Id", sessionId);
        }

        return http.SendAsync(message);
    }

    private static string ExtractSseData(string body)
    {
        var data = body
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
            .Select(line => line["data:".Length..].Trim())
            .LastOrDefault(line => line.Length > 0);
        return data ?? throw new InvalidOperationException("No data in the SSE response.");
    }
}
