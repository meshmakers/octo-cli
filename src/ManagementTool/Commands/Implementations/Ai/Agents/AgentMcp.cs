using System.Text.Json.Nodes;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Knowledge;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Ai.Agents;

/// <summary>
///     The shared part of the agent commands (<c>CreateAgent</c>, <c>UpdateAgent</c>, <c>EnableAgent</c>,
///     <c>DisableAgent</c>, <c>GetAgents</c>): each calls one agent tool of the MCP service on the tenant route with the
///     signed-in user's own token, so the checks stay in octo-mcp-service (tenant administrators only, agents never
///     manage agents, grants must name existing tools, the client secret is never returned). Reuses the MCP client and
///     the endpoint, token and refusal handling of the knowledge platform-update commands; the URL comes only from the
///     context (<c>McpServiceUrl</c>).
/// </summary>
internal static class AgentMcp
{
    public const string CreateTool = "create_agent";
    public const string UpdateTool = "update_agent";
    public const string SetEnabledTool = "set_agent_enabled";
    public const string ListTool = "list_agents";
    public const string GetTool = "get_agent";

    /// <summary>The tenant from <c>-tid</c> or the context, lower case.</summary>
    public static string TenantId(ICommandArgumentValue arguments, IArgument tenantIdArg, OctoToolOptions options) =>
        KnowledgePlatformUpdatesCommandSupport.TenantId(arguments, tenantIdArg, options);

    /// <summary>
    ///     Calls <paramref name="toolName" /> on <c>&lt;McpServiceUrl of the context&gt;/&lt;tenant&gt;/mcp</c> with the
    ///     user's token (exchanged for another tenant) and returns the successful answer; a refusal throws a
    ///     <see cref="ToolException" /> with the tool's error code and message. The token is never logged.
    /// </summary>
    public static async Task<JsonObject> CallAsync(IMcpToolClient mcpToolClient, OctoToolOptions options,
        string? contextToken, ITenantTokenProvider? tenantTokenProvider, string tenantId, string toolName,
        JsonObject arguments, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(options.McpServiceUrl))
        {
            throw new ToolException(
                "No MCP service URL on the context: set it with octo-cli -c AddContext -n <name> -msu <url> or " +
                "Config -msu <url>. The MCP service runs the agent checks; the CLI calls it with your token.");
        }

        var endpoint = KnowledgePlatformUpdatesMcp.Endpoint(options, tenantId);
        var token = await KnowledgePlatformUpdatesMcp.TokenForTenantAsync(contextToken, tenantId, tenantTokenProvider);
        arguments["tenantId"] = tenantId;

        logger.LogInformation("Calling MCP tool '{Tool}' for tenant '{TenantId}' at {Endpoint}", toolName, tenantId,
            endpoint);
        var result = KnowledgePlatformUpdatesMcp.AnswerObject(
            await mcpToolClient.CallToolAsync(endpoint, token, toolName, arguments), toolName);
        if (KnowledgePlatformUpdatesMcp.IsSuccess(result))
        {
            return result;
        }

        var errorCode = result["errorCode"] is JsonValue code && code.TryGetValue<string>(out var text) ? text : null;
        throw KnowledgePlatformUpdatesMcp.Refused(result, toolName, RefusalOutcome(toolName, errorCode));
    }

    /// <summary>
    ///     What a refusal means for the tenant. <c>update_agent</c> and <c>set_agent_enabled</c> can refuse after a partial
    ///     change (the agent was updated or disabled, its identity client was not: <c>IdentityFailed</c>), so the command
    ///     must not claim that nothing changed; their message says what did. <c>create_agent</c> rolls its writes back on a
    ///     failure after the first write (<c>IdentityFailed</c>, <c>Failed</c>), but when the rollback fails too, a client,
    ///     agent or mandate can remain and the tool's message names it; only its refusals before any write (validation,
    ///     <c>AgentExists</c>, <c>NotTenantAdministrator</c>, ...) change nothing. The reads change nothing.
    /// </summary>
    public static string RefusalOutcome(string toolName, string? errorCode = null) => toolName switch
    {
        UpdateTool or SetEnabledTool =>
            "The tool's message says what was changed; a refusal after a partial change is possible, so check the agent with GetAgents.",
        CreateTool when errorCode is "IdentityFailed" or "Failed" or null =>
            "The tool's message says what was kept; when it names a client, agent or mandate that remains, delete it by hand, then check with GetAgents.",
        _ => "Nothing was changed by this command."
    };

    /// <summary>
    ///     Parses a tool grant <c>&lt;toolPattern&gt;[:&lt;maxRisk&gt;[:&lt;argumentConstraints JSON&gt;]]</c>, e.g.
    ///     <c>knowledge_*:Medium</c> or <c>query_entities:Low:{"ckTypeId":["System.Ai/KnowledgePage"]}</c>.
    /// </summary>
    public static JsonObject ParseGrant(string value)
    {
        var parts = value.Split(':', 3);
        var pattern = parts[0].Trim();
        if (pattern.Length == 0)
        {
            throw new ToolException($"The tool grant '{value}' names no tool: use <toolPattern>[:<maxRisk>[:<JSON>]].");
        }

        var grant = new JsonObject { ["toolPattern"] = pattern };
        if (parts.Length > 1 && parts[1].Trim().Length > 0)
        {
            grant["maxRisk"] = parts[1].Trim();
        }

        if (parts.Length > 2 && parts[2].Trim().Length > 0)
        {
            var constraints = parts[2].Trim();
            JsonNode? parsed;
            try
            {
                parsed = JsonNode.Parse(constraints);
            }
            catch (System.Text.Json.JsonException)
            {
                parsed = null;
            }

            if (parsed is not JsonObject)
            {
                throw new ToolException($"The argument constraints of the tool grant '{pattern}' are not a JSON object.");
            }

            grant["argumentConstraints"] = constraints;
        }

        return grant;
    }

    /// <summary>The values of a repeatable argument, or null when it was not used.</summary>
    public static List<string>? Values(ICommandArgumentValue arguments, IArgument argument) =>
        arguments.IsArgumentUsed(argument) ? arguments.GetArgumentValue(argument).Values.ToList() : null;

    /// <summary>A scalar string argument, or null when it was not used.</summary>
    public static string? Text(ICommandArgumentValue arguments, IArgument argument) =>
        arguments.IsArgumentUsed(argument) ? arguments.GetArgumentScalarValue<string>(argument) : null;

    /// <summary>The instructions from <c>-i</c> or the file of <c>-if</c>; null when neither was used.</summary>
    public static async Task<string?> InstructionsAsync(ICommandArgumentValue arguments, IArgument textArg,
        IArgument fileArg)
    {
        var text = Text(arguments, textArg);
        var file = Text(arguments, fileArg);
        if (text != null && file != null)
        {
            throw new ToolException("Pass the instructions either as text (-i) or as a file (-if), not both.");
        }

        if (file == null)
        {
            return text;
        }

        if (!File.Exists(file))
        {
            throw new ToolException($"The instructions file '{file}' does not exist.");
        }

        return await File.ReadAllTextAsync(file);
    }

    /// <summary>Prints the answer as JSON, or the agent of a single-agent answer in lines.</summary>
    public static void PrintAgentAnswer(IConsoleService console, JsonObject answer, bool json, string headline)
    {
        if (json)
        {
            console.WriteLine(answer.ToJsonString());
            return;
        }

        console.WriteLine(headline);
        if (answer["message"] is JsonValue message && message.TryGetValue<string>(out var note) &&
            !string.IsNullOrWhiteSpace(note))
        {
            console.WriteLine($"  Note: {note}");
        }

        if (answer["agent"] is JsonObject agent)
        {
            PrintAgent(console, agent, detailed: true);
        }

        var roles = answer["assignedRoles"] is JsonArray assigned
            ? assigned.Select(r => r?.GetValue<string>()).Where(r => !string.IsNullOrEmpty(r)).ToList()
            : [];
        if (roles.Count > 0)
        {
            console.WriteLine($"  Roles assigned to the service-account client: {string.Join(", ", roles)}");
        }
    }

    /// <summary>Prints one agent; <paramref name="detailed" /> adds instructions and the mandate's grants.</summary>
    public static void PrintAgent(IConsoleService console, JsonObject agent, bool detailed)
    {
        var enabled = KnowledgePlatformUpdatesMcp.Flag(agent, "enabled") ? "enabled" : "disabled";
        var mandate = agent["mandate"] as JsonObject;
        console.WriteLine($"  {KnowledgePlatformUpdatesMcp.Text(agent, "name")}  ({KnowledgePlatformUpdatesMcp.Text(agent, "rtId")})  " +
                          $"{enabled}  v{KnowledgePlatformUpdatesMcp.Number(agent, "version")}  " +
                          $"{KnowledgePlatformUpdatesMcp.Text(agent, "autonomyLevel")}  " +
                          $"client {KnowledgePlatformUpdatesMcp.Text(agent, "serviceAccountClientId") ?? "-"}" +
                          (mandate == null
                              ? "  no mandate"
                              : $"  ceiling {KnowledgePlatformUpdatesMcp.Text(mandate, "riskCeiling")}, " +
                                $"budget {KnowledgePlatformUpdatesMcp.Number(mandate, "changeBudgetPerRun")}, " +
                                $"{KnowledgePlatformUpdatesMcp.Text(mandate, "writeMode")}, " +
                                $"{KnowledgePlatformUpdatesMcp.Items(mandate, "toolGrants").Count()} grant(s)"));
        if (!detailed)
        {
            return;
        }

        console.WriteLine($"    Model: {KnowledgePlatformUpdatesMcp.Text(agent, "model") ?? "(tenant default)"}, runner: " +
                          $"{KnowledgePlatformUpdatesMcp.Text(agent, "runnerKind")}, sponsor: " +
                          $"{KnowledgePlatformUpdatesMcp.Text(agent, "sponsorUserId") ?? "-"}");
        if (agent["serviceAccountClientEnabled"] is JsonValue clientEnabled && clientEnabled.TryGetValue<bool>(out var on))
        {
            console.WriteLine($"    Service-account client: {(on ? "enabled" : "disabled")}");
        }

        if (mandate != null)
        {
            foreach (var grant in KnowledgePlatformUpdatesMcp.Items(mandate, "toolGrants"))
            {
                var constraints = KnowledgePlatformUpdatesMcp.Text(grant, "argumentConstraints");
                console.WriteLine($"    Grant: {KnowledgePlatformUpdatesMcp.Text(grant, "toolPattern")} up to " +
                                  $"{KnowledgePlatformUpdatesMcp.Text(grant, "maxRisk") ?? "Low"}" +
                                  (string.IsNullOrEmpty(constraints) ? "" : $" {constraints}"));
            }

            if (mandate["allowedCkTypes"] is JsonArray types)
            {
                console.WriteLine($"    Writable CK types: {string.Join(", ", types.Select(t => t?.GetValue<string>()))}");
            }
        }

        var instructions = KnowledgePlatformUpdatesMcp.Text(agent, "instructions");
        if (!string.IsNullOrEmpty(instructions))
        {
            var firstLine = instructions.Split('\n')[0].Trim();
            console.WriteLine($"    Instructions: {(firstLine.Length > 100 ? firstLine[..100] + "..." : firstLine)}");
        }
    }
}
