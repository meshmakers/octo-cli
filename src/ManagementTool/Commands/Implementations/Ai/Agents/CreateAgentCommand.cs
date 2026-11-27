using System.Text.Json.Nodes;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Ai.Agents;

/// <summary>
///     Creates an agent of the tenant with its mandate and service-account client through the MCP tool
///     <c>create_agent</c>, with the signed-in user's token.
/// </summary>
internal class CreateAgentCommand : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IArgument _allowedCkTypeArg;
    private readonly IOptions<OctoToolAuthenticationOptions> _authenticationOptions;
    private readonly IArgument _autonomyLevelArg;
    private readonly IArgument _changeBudgetArg;
    private readonly IArgument _clientIdArg;
    private readonly IConsoleService _consoleService;
    private readonly IArgument _disabledArg;
    private readonly IArgument _grantArg;
    private readonly IArgument _instructionsArg;
    private readonly IArgument _instructionsFileArg;
    private readonly IArgument _jsonArg;
    private readonly IArgument _mandateArg;
    private readonly IMcpToolClient _mcpToolClient;
    private readonly IArgument _modelArg;
    private readonly IArgument _nameArg;
    private readonly IArgument _riskCeilingArg;
    private readonly IArgument _roleArg;
    private readonly IArgument _runnerKindArg;
    private readonly IArgument _sponsorArg;
    private readonly IArgument _tenantIdArg;
    private readonly ITenantTokenProvider? _tenantTokenProvider;
    private readonly IArgument _writeModeArg;

    public CreateAgentCommand(ILogger<CreateAgentCommand> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IAuthenticationService authenticationService,
        IOptions<OctoToolAuthenticationOptions> authenticationOptions, IMcpToolClient mcpToolClient,
        IConsoleService consoleService, ITenantTokenProvider? tenantTokenProvider = null)
        : base(logger, Constants.AiServicesGroup, "CreateAgent",
            "Creates an agent of the tenant with its mandate and its service-account client (tenant administrators " +
            "only). Calls the MCP tool create_agent with your token; the client secret is never shown.",
            options, assetServicesClient, authenticationService)
    {
        _authenticationOptions = authenticationOptions;
        _mcpToolClient = mcpToolClient;
        _consoleService = consoleService;
        _tenantTokenProvider = tenantTokenProvider;

        _nameArg = CommandArgumentValue.AddArgument("n", "name", ["Name of the agent, unique in the tenant"], true, 1);
        _instructionsArg = CommandArgumentValue.AddArgument("i", "instructions",
            ["Persona and standing instructions of the agent. Never put a credential here. Or use -if"], false, 1);
        _instructionsFileArg = CommandArgumentValue.AddArgument("if", "instructionsFile",
            ["File with the instructions of the agent (instead of -i)"], false, 1);
        _modelArg = CommandArgumentValue.AddArgument("m", "model",
            ["Model id, e. g. 'claude-sonnet-4-6'. Default: the tenant default"], false, 1);
        _autonomyLevelArg = CommandArgumentValue.AddArgument("al", "autonomyLevel",
            ["ProposeOnly (default) or ActWithinBudget"], false, 1);
        _runnerKindArg = CommandArgumentValue.AddArgument("rk", "runnerKind",
            ["AgentSdk (default) or ClaudeCodeCli"], false, 1);
        _sponsorArg = CommandArgumentValue.AddArgument("su", "sponsorUserId",
            ["User id of the accountable person. Default: you"], false, 1);
        _disabledArg = CommandArgumentValue.AddArgument("d", "disabled",
            ["Create the agent disabled (it starts no run until EnableAgent)"], false, 0);
        _mandateArg = CommandArgumentValue.AddArgument("mid", "mandateRtId",
            ["Runtime id of an existing mandate; leave the mandate values (-g, -rc, -cb, -wm, -ct) out then"], false, 1);
        _grantArg = CommandArgumentValue.AddArgument("g", "grant",
            [
                "Tool grant of the new mandate: <toolPattern>[:<maxRisk>[:<argumentConstraints JSON>]], e. g.",
                "'knowledge_*:Medium'. Repeat for several grants. Default: no grant"
            ], false, 1, true);
        _riskCeilingArg = CommandArgumentValue.AddArgument("rc", "riskCeiling",
            ["Highest risk class of any tool call: Low (default), Medium, High"], false, 1);
        _changeBudgetArg = CommandArgumentValue.AddArgument("cb", "changeBudgetPerRun",
            ["Executed changes one run may make (default 0: no direct change)"], false, 1);
        _writeModeArg = CommandArgumentValue.AddArgument("wm", "writeMode",
            ["Propose (default: every granted write becomes a proposal) or Direct"], false, 1);
        _allowedCkTypeArg = CommandArgumentValue.AddArgument("ct", "allowedCkType",
            ["CK type id the agent may write, e. g. 'System.Ai/KnowledgePage'. Repeat for several"], false, 1, true);
        _clientIdArg = CommandArgumentValue.AddArgument("cid", "serviceAccountClientId",
            ["client_id of the service-account client. Default: 'agent-' plus the name in lower-case words"], false, 1);
        _roleArg = CommandArgumentValue.AddArgument("r", "role",
            ["Role name or id for the service-account client. Repeat for several. Default: none"], false, 1, true);
        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId",
            ["Id of the tenant. Default: the tenant of the context"], false, 1);
        _jsonArg = CommandArgumentValue.AddArgument("json", "json",
            ["Print the answer of the MCP tool as JSON"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(
                    arguments:
                    [
                        new CodeSampleArgument(_nameArg, "Knowledge curator"),
                        new CodeSampleArgument(_instructionsFileArg, "./curator.md"),
                        new CodeSampleArgument(_grantArg, "knowledge_*:Medium"),
                        new CodeSampleArgument(_riskCeilingArg, "Medium")
                    ],
                    description: "Create an agent that may call the knowledge tools; every write becomes a proposal")
            ],
            Notes:
            [
                "Tenant administrators only (role TenantManagement or UserManagement); an agent's own token is refused. " +
                "The MCP service checks every grant against its tool list (UnknownTool) and the risk ceiling.",
                "The defaults are deliberately narrow: no grants, risk ceiling Low, change budget 0, write mode Propose.",
                "There is no option for the client secret: the service generates one and never returns it, so no secret " +
                "lands in a shell history or process list.",
                "Calls <McpServiceUrl of the context>/<tenant>/mcp (set it once with AddContext -msu or Config -msu)."
            ]);

    public override async Task Execute()
    {
        var tenantId = AgentMcp.TenantId(CommandArgumentValue, _tenantIdArg, Options.Value);
        var instructions = await AgentMcp.InstructionsAsync(CommandArgumentValue, _instructionsArg, _instructionsFileArg);
        if (string.IsNullOrWhiteSpace(instructions))
        {
            throw new ToolException("An agent needs instructions: pass -i <text> or -if <file>.");
        }

        var arguments = new JsonObject
        {
            ["name"] = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg),
            ["instructions"] = instructions,
            ["enabled"] = !CommandArgumentValue.IsArgumentUsed(_disabledArg)
        };
        AddText(arguments, "model", _modelArg);
        AddText(arguments, "autonomyLevel", _autonomyLevelArg);
        AddText(arguments, "runnerKind", _runnerKindArg);
        AddText(arguments, "sponsorUserId", _sponsorArg);
        AddText(arguments, "mandateRtId", _mandateArg);
        AddText(arguments, "riskCeiling", _riskCeilingArg);
        AddText(arguments, "writeMode", _writeModeArg);
        AddText(arguments, "serviceAccountClientId", _clientIdArg);
        if (CommandArgumentValue.IsArgumentUsed(_changeBudgetArg))
        {
            arguments["changeBudgetPerRun"] = CommandArgumentValue.GetArgumentScalarValue<int>(_changeBudgetArg);
        }

        if (AgentMcp.Values(CommandArgumentValue, _grantArg) is { } grants)
        {
            arguments["toolGrants"] = new JsonArray(grants.Select(g => (JsonNode)AgentMcp.ParseGrant(g)).ToArray());
        }

        if (AgentMcp.Values(CommandArgumentValue, _allowedCkTypeArg) is { } types)
        {
            arguments["allowedCkTypes"] = new JsonArray(types.Select(t => (JsonNode)JsonValue.Create(t)!).ToArray());
        }

        if (AgentMcp.Values(CommandArgumentValue, _roleArg) is { } roles)
        {
            arguments["roles"] = new JsonArray(roles.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray());
        }

        var answer = await AgentMcp.CallAsync(_mcpToolClient, Options.Value, _authenticationOptions.Value.AccessToken,
            _tenantTokenProvider, tenantId, AgentMcp.CreateTool, arguments, Logger);
        AgentMcp.PrintAgentAnswer(_consoleService, answer, CommandArgumentValue.IsArgumentUsed(_jsonArg),
            $"Agent created in tenant '{tenantId}':");
    }

    private void AddText(JsonObject arguments, string name, IArgument argument)
    {
        if (AgentMcp.Text(CommandArgumentValue, argument) is { } value)
        {
            arguments[name] = value;
        }
    }
}
