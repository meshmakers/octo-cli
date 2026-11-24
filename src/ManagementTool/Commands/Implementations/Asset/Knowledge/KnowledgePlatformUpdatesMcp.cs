using System.Text.Json.Nodes;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Knowledge;

/// <summary>
///     The shared part of <see cref="ListKnowledgePlatformUpdatesCommand" /> and
///     <see cref="AdoptKnowledgePlatformUpdatesCommand" />: both call the knowledge platform-update tools of the MCP
///     service on the tenant route with the signed-in user's own token, so the CLI applies exactly the rules of those
///     tools (unedited Blueprint pages are switched, edited pages are never changed) instead of a copy of them.
/// </summary>
internal static class KnowledgePlatformUpdatesMcp
{
    public const string ListTool = "knowledge_list_platform_updates";
    public const string AdoptTool = "knowledge_adopt_platform_updates";

    /// <summary>The tenant route of the MCP service from the argument or the context's <c>McpServiceUrl</c>.</summary>
    public static Uri Endpoint(string? argumentUrl, OctoToolOptions options, string tenantId)
    {
        var url = string.IsNullOrWhiteSpace(argumentUrl) ? options.McpServiceUrl : argumentUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ToolException(
                "No MCP service URL: set it on the context (octo-cli -c AddContext -n <name> -msu <url>, or Config -msu " +
                "<url>) or pass -msu <url>. The MCP service runs the knowledge rules; the CLI calls it with your token.");
        }

        return McpToolClient.TenantEndpoint(url, tenantId);
    }

    /// <summary>
    ///     The token the target tenant sees: the context token, or the token exchanged from it for another tenant (the same
    ///     cached exchange the service clients use). Never falls back to a token of another tenant.
    /// </summary>
    public static async Task<string> TokenForTenantAsync(string? contextToken, string tenantId,
        ITenantTokenProvider? tenantTokenProvider)
    {
        if (string.IsNullOrWhiteSpace(contextToken))
        {
            throw new ToolException("Not logged in: run octo-cli -c LogIn first.");
        }

        if (!AccessTokenInspector.IsIssuedForOtherTenant(contextToken, tenantId))
        {
            return contextToken;
        }

        if (tenantTokenProvider == null)
        {
            throw new ToolException($"No token for tenant '{tenantId}' is available; use a context of that tenant.");
        }

        try
        {
            return await tenantTokenProvider.GetAccessTokenAsync(tenantId, contextToken);
        }
        catch (TenantTokenExchangeDeniedException ex)
        {
            throw new ToolException(
                $"No token for tenant '{tenantId}' could be obtained from the signed-in user: {ex.Message} " +
                $"Provision yourself there (ProvisionCurrentUser -ttid {tenantId}) or use a context of that tenant.", ex);
        }
    }

    /// <summary>
    ///     The answer of a knowledge tool as an object; a refused call (<c>isSuccess: false</c>) becomes a
    ///     <see cref="ToolException" /> with the tool's error code and message.
    /// </summary>
    public static JsonObject RequireSuccess(JsonNode? answer, string toolName)
    {
        if (answer is not JsonObject result)
        {
            throw new ToolException($"The MCP tool '{toolName}' sent no answer object.");
        }

        if (result["isSuccess"] is JsonValue ok && ok.TryGetValue<bool>(out var success) && success)
        {
            return result;
        }

        var code = Text(result, "errorCode");
        throw new ToolException($"The MCP tool '{toolName}' refused" + (code == null ? "" : $" ({code})") + ": " +
                                (Text(result, "errorMessage") ?? "no message") + ". Nothing was changed by this command.");
    }

    public static string? Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    public static int Number(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0;

    public static bool Flag(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    public static IEnumerable<JsonObject> Items(JsonObject node, string name) =>
        node[name] is JsonArray array ? array.OfType<JsonObject>() : [];
}
