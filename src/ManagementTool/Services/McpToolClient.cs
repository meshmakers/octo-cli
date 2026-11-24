using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

/// <summary>
///     Calls one tool of the OctoMesh MCP service (octo-mcp-service) on a tenant route, as the signed-in user. Used by
///     commands whose rule lives in an MCP tool, so the CLI applies exactly the same rule instead of a copy of it.
/// </summary>
public interface IMcpToolClient
{
    /// <summary>
    ///     Opens an MCP session on <paramref name="endpoint" />, calls <paramref name="toolName" /> with
    ///     <paramref name="arguments" /> and returns the tool's structured answer.
    /// </summary>
    /// <exception cref="ToolException">
    ///     The server refused or failed, or the tool returned an error result. The message never contains the token.
    /// </exception>
    Task<JsonNode?> CallToolAsync(Uri endpoint, string accessToken, string toolName, JsonObject arguments,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     A minimal MCP Streamable HTTP client: <c>initialize</c>, <c>notifications/initialized</c>, <c>tools/call</c>,
///     answers as plain JSON or as an SSE stream. The bearer token goes only into the <c>Authorization</c> header; every
///     text of an answer that reaches an exception is redacted (the token never appears in output, at any log level,
///     also Trace in <c>nlog.config</c>).
/// </summary>
internal sealed class McpToolClient : IMcpToolClient, IDisposable
{
    /// <summary>The MCP protocol version sent in <c>initialize</c>.</summary>
    internal const string ProtocolVersion = "2025-03-26";

    private const int MaxQuotedLength = 500;

    private readonly HttpClient _httpClient;

    /// <summary>Creates the client; tests pass a stub handler.</summary>
    public McpToolClient(HttpMessageHandler? handler = null)
    {
        _httpClient = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    /// <inheritdoc />
    public void Dispose() => _httpClient.Dispose();

    /// <summary>
    ///     The tenant route of the MCP service: <c>{base}/{tenantId}/mcp</c>. A trailing tenantless <c>/mcp</c> of the
    ///     configured URL is cut, so both <c>https://mcp.example.com/</c> and <c>https://mcp.example.com/mcp</c> work.
    /// </summary>
    public static Uri TenantEndpoint(string mcpServiceUrl, string tenantId)
    {
        if (!Uri.TryCreate(mcpServiceUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ToolException($"The MCP service URL '{mcpServiceUrl}' is not an absolute http(s) URL.");
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^"/mcp".Length];
        }

        return new Uri($"{uri.GetLeftPart(UriPartial.Authority)}{path}/{Uri.EscapeDataString(tenantId)}/mcp");
    }

    /// <inheritdoc />
    public async Task<JsonNode?> CallToolAsync(Uri endpoint, string accessToken, string toolName, JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        var initialize = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "initialize",
            ["params"] = new JsonObject
            {
                ["protocolVersion"] = ProtocolVersion,
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject { ["name"] = "octo-cli", ["version"] = "1.0.0" }
            }
        };
        var (_, sessionId) = await SendRpcAsync(endpoint, accessToken, null, initialize, 1, "initialize", cancellationToken);

        // A notification: the server answers 202 without a body; the outcome is not checked.
        using (var initialized = CreateRequest(endpoint, accessToken, sessionId,
                   new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" }))
        {
            using var _ = await _httpClient.SendAsync(initialized, cancellationToken);
        }

        var call = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 2,
            ["method"] = "tools/call",
            ["params"] = new JsonObject { ["name"] = toolName, ["arguments"] = arguments }
        };
        var (result, _) = await SendRpcAsync(endpoint, accessToken, sessionId, call, 2, $"tool '{toolName}'",
            cancellationToken);
        return ReadToolResult(toolName, result, accessToken);
    }

    private static HttpRequestMessage CreateRequest(Uri endpoint, string accessToken, string? sessionId, JsonObject body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (sessionId != null)
        {
            request.Headers.Add("Mcp-Session-Id", sessionId);
        }

        return request;
    }

    private async Task<(JsonObject Result, string? SessionId)> SendRpcAsync(Uri endpoint, string accessToken,
        string? sessionId, JsonObject body, int id, string what, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(endpoint, accessToken, sessionId, body);
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ToolException($"The MCP service at {endpoint} cannot be reached ({what}): {Redact(ex.Message, accessToken)}");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessionIds))
            {
                sessionId = sessionIds.FirstOrDefault() ?? sessionId;
            }

            var message = FindResponse(text, id);
            if (message?["error"] is JsonObject error)
            {
                var code = error["code"]?.ToJsonString() ?? "?";
                var errorMessage = error["message"] is JsonValue value && value.TryGetValue<string>(out var s) ? s : "no message";
                throw new ToolException(Redact($"The MCP service answered {what} with error {code}: {errorMessage}", accessToken));
            }

            if (!response.IsSuccessStatusCode)
            {
                var hint = response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                    ? " Log in again (octo-cli -c LogIn) with a user of the tenant, or check the MCP service URL of the context."
                    : "";
                throw new ToolException(Redact(
                    $"The MCP service answered {what} with HTTP {(int)response.StatusCode} {response.ReasonPhrase}." + hint +
                    (string.IsNullOrWhiteSpace(text) ? "" : $" {Truncate(Redact(text, accessToken))}"), accessToken));
            }

            if (message?["result"] is not JsonObject result)
            {
                throw new ToolException(Redact(
                    $"The MCP service sent no JSON-RPC result for {what}: {Truncate(Redact(text, accessToken))}", accessToken));
            }

            return (result, sessionId);
        }
    }

    /// <summary>The JSON-RPC message answering request <paramref name="id" /> in a plain JSON body or an SSE stream.</summary>
    internal static JsonObject? FindResponse(string body, int id)
    {
        var trimmed = body.TrimStart();
        if (trimmed.StartsWith('{'))
        {
            return TryParse(trimmed) as JsonObject;
        }

        JsonObject? fallback = null;
        foreach (var line in body.Split('\n'))
        {
            var candidate = line.Trim();
            if (!candidate.StartsWith("data:", StringComparison.Ordinal) ||
                TryParse(candidate["data:".Length..].Trim()) is not JsonObject message)
            {
                continue;
            }

            if (message["id"] is JsonValue value && value.TryGetValue<int>(out var messageId) && messageId == id)
            {
                return message;
            }

            if (message["result"] != null || message["error"] != null)
            {
                fallback ??= message;
            }
        }

        return fallback;
    }

    /// <summary>The structured answer of a <c>tools/call</c> result; a tool error result throws.</summary>
    internal static JsonNode? ReadToolResult(string toolName, JsonObject result, string accessToken)
    {
        var texts = result["content"] is JsonArray content
            ? content.OfType<JsonObject>()
                .Where(item => item["type"]?.GetValue<string>() == "text")
                .Select(item => item["text"]?.GetValue<string>() ?? string.Empty)
                .ToList()
            : [];

        if (result["isError"] is JsonValue isError && isError.TryGetValue<bool>(out var failed) && failed)
        {
            throw new ToolException($"The MCP tool '{toolName}' returned an error: " +
                                    Truncate(Redact(string.Join("\n", texts), accessToken)));
        }

        if (result["structuredContent"] is { } structured)
        {
            return structured.DeepClone();
        }

        return texts.Count == 1 ? TryParse(texts[0]) : null;
    }

    private static JsonNode? TryParse(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // Redact before truncating: a cut through the token would leave part of it in the text.
    private static string Redact(string text, string accessToken) =>
        text.Replace(accessToken, "***", StringComparison.Ordinal);

    private static string Truncate(string text) => text.Length <= MaxQuotedLength ? text : text[..MaxQuotedLength] + "...";
}
