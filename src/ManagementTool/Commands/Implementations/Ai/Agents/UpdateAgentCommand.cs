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
///     Updates an agent of the tenant through the MCP tool <c>update_agent</c>, with the signed-in user's token. Every
///     option is optional; an omitted one keeps its value.
/// </summary>
internal class UpdateAgentCommand : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IArgument _agentArg;
    private readonly IArgument _allowedCkTypeArg;
    private readonly IOptions<OctoToolAuthenticationOptions> _authenticationOptions;
    private readonly IArgument _autonomyLevelArg;
    private readonly IArgument _changeBudgetArg;
    private readonly IArgument _clearCkTypesArg;
    private readonly IArgument _clearGrantsArg;
    private readonly IArgument _clearModelArg;
    private readonly IArgument _clearRolesArg;
    private readonly IConsoleService _consoleService;
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

    public UpdateAgentCommand(ILogger<UpdateAgentCommand> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IAuthenticationService authenticationService,
        IOptions<OctoToolAuthenticationOptions> authenticationOptions, IMcpToolClient mcpToolClient,
        IConsoleService consoleService, ITenantTokenProvider? tenantTokenProvider = null)
        : base(logger, Constants.AiServicesGroup, "UpdateAgent",
            "Updates an agent of the tenant (tenant administrators only); omitted options keep their value. Calls the " +
            "MCP tool update_agent with your token.",
            options, assetServicesClient, authenticationService)
    {
        _authenticationOptions = authenticationOptions;
        _mcpToolClient = mcpToolClient;
        _consoleService = consoleService;
        _tenantTokenProvider = tenantTokenProvider;

        _agentArg = CommandArgumentValue.AddArgument("a", "agent", ["Runtime id or name of the agent"], true, 1);
        _nameArg = CommandArgumentValue.AddArgument("n", "name", ["New name (unique in the tenant)"], false, 1);
        _instructionsArg = CommandArgumentValue.AddArgument("i", "instructions",
            ["New persona and standing instructions. Or use -if"], false, 1);
        _instructionsFileArg = CommandArgumentValue.AddArgument("if", "instructionsFile",
            ["File with the new instructions (instead of -i)"], false, 1);
        _modelArg = CommandArgumentValue.AddArgument("m", "model",
            ["New model id"], false, 1);
        _clearModelArg = CommandArgumentValue.AddArgument("cm", "clearModel",
            ["Go back to the tenant default model"], false, 0);
        _autonomyLevelArg = CommandArgumentValue.AddArgument("al", "autonomyLevel",
            ["ProposeOnly or ActWithinBudget"], false, 1);
        _runnerKindArg = CommandArgumentValue.AddArgument("rk", "runnerKind", ["AgentSdk or ClaudeCodeCli"], false, 1);
        _sponsorArg = CommandArgumentValue.AddArgument("su", "sponsorUserId", ["New sponsor user id"], false, 1);
        _mandateArg = CommandArgumentValue.AddArgument("mid", "mandateRtId",
            ["Switch the agent to another existing mandate; leave the mandate values out then"], false, 1);
        _grantArg = CommandArgumentValue.AddArgument("g", "grant",
            [
                "Tool grant <toolPattern>[:<maxRisk>[:<argumentConstraints JSON>]]. Repeat for several; the given",
                "grants replace the list"
            ], false, 1, true);
        _clearGrantsArg = CommandArgumentValue.AddArgument("cg", "clearGrants",
            ["Remove every tool grant of the agent's mandate"], false, 0);
        _riskCeilingArg = CommandArgumentValue.AddArgument("rc", "riskCeiling", ["New risk ceiling: Low, Medium, High"],
            false, 1);
        _changeBudgetArg = CommandArgumentValue.AddArgument("cb", "changeBudgetPerRun",
            ["New change budget per run (0 or more)"], false, 1);
        _writeModeArg = CommandArgumentValue.AddArgument("wm", "writeMode", ["Propose or Direct"], false, 1);
        _allowedCkTypeArg = CommandArgumentValue.AddArgument("ct", "allowedCkType",
            ["Writable CK type id. Repeat for several; the given types replace the list"], false, 1, true);
        _clearCkTypesArg = CommandArgumentValue.AddArgument("cct", "clearCkTypes",
            ["Clear the list of writable CK types (the grants decide alone)"], false, 0);
        _roleArg = CommandArgumentValue.AddArgument("r", "role",
            ["Role name or id the service-account client gets instead of its current roles. Repeat for several"],
            false, 1, true);
        _clearRolesArg = CommandArgumentValue.AddArgument("cr", "clearRoles",
            ["Remove every role of the service-account client"], false, 0);
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
                        new CodeSampleArgument(_agentArg, "Knowledge curator"),
                        new CodeSampleArgument(_grantArg, "knowledge_*:Medium"),
                        new CodeSampleArgument(_changeBudgetArg, "5")
                    ],
                    description: "Replace the grants and allow five executed changes per run")
            ],
            Notes:
            [
                "Tenant administrators only; agents never manage agents. Grants are checked like in CreateAgent " +
                "(UnknownTool, RiskAboveCeiling). A mandate shared by several agents is not changed: pass -mid instead.",
                "A change of instructions, model or mandate raises the agent's version. Use EnableAgent and DisableAgent " +
                "to switch the agent on or off.",
                "Calls <McpServiceUrl of the context>/<tenant>/mcp (set it once with AddContext -msu or Config -msu)."
            ]);

    public override async Task Execute()
    {
        var tenantId = AgentMcp.TenantId(CommandArgumentValue, _tenantIdArg, Options.Value);
        RefuseBoth(_grantArg, _clearGrantsArg);
        RefuseBoth(_allowedCkTypeArg, _clearCkTypesArg);
        RefuseBoth(_roleArg, _clearRolesArg);
        RefuseBoth(_modelArg, _clearModelArg);

        var arguments = new JsonObject { ["agent"] = CommandArgumentValue.GetArgumentScalarValue<string>(_agentArg) };
        if (await AgentMcp.InstructionsAsync(CommandArgumentValue, _instructionsArg, _instructionsFileArg) is { } instructions)
        {
            arguments["instructions"] = instructions;
        }

        AddText(arguments, "name", _nameArg);
        AddText(arguments, "model", _modelArg);
        if (CommandArgumentValue.IsArgumentUsed(_clearModelArg))
        {
            arguments["model"] = "";
        }

        AddText(arguments, "autonomyLevel", _autonomyLevelArg);
        AddText(arguments, "runnerKind", _runnerKindArg);
        AddText(arguments, "sponsorUserId", _sponsorArg);
        AddText(arguments, "mandateRtId", _mandateArg);
        AddText(arguments, "riskCeiling", _riskCeilingArg);
        AddText(arguments, "writeMode", _writeModeArg);
        if (CommandArgumentValue.IsArgumentUsed(_changeBudgetArg))
        {
            arguments["changeBudgetPerRun"] = CommandArgumentValue.GetArgumentScalarValue<int>(_changeBudgetArg);
        }

        AddList(arguments, "toolGrants", _grantArg, _clearGrantsArg, g => AgentMcp.ParseGrant(g));
        AddList(arguments, "allowedCkTypes", _allowedCkTypeArg, _clearCkTypesArg, t => JsonValue.Create(t)!);
        AddList(arguments, "roles", _roleArg, _clearRolesArg, r => JsonValue.Create(r)!);

        if (arguments.Count == 1)
        {
            throw new ToolException("Nothing to update: pass at least one option to change (see --help).");
        }

        var answer = await AgentMcp.CallAsync(_mcpToolClient, Options.Value, _authenticationOptions.Value.AccessToken,
            _tenantTokenProvider, tenantId, AgentMcp.UpdateTool, arguments, Logger);
        AgentMcp.PrintAgentAnswer(_consoleService, answer, CommandArgumentValue.IsArgumentUsed(_jsonArg),
            $"Agent updated in tenant '{tenantId}':");
    }

    private void RefuseBoth(IArgument values, IArgument clear)
    {
        if (CommandArgumentValue.IsArgumentUsed(values) && CommandArgumentValue.IsArgumentUsed(clear))
        {
            throw new ToolException($"Pass either --{values.LongTerm} or --{clear.LongTerm}, not both.");
        }
    }

    private void AddText(JsonObject arguments, string name, IArgument argument)
    {
        if (AgentMcp.Text(CommandArgumentValue, argument) is { } value)
        {
            arguments[name] = value;
        }
    }

    private void AddList(JsonObject arguments, string name, IArgument values, IArgument clear,
        Func<string, JsonNode> convert)
    {
        if (CommandArgumentValue.IsArgumentUsed(clear))
        {
            arguments[name] = new JsonArray();
        }
        else if (AgentMcp.Values(CommandArgumentValue, values) is { } list)
        {
            arguments[name] = new JsonArray(list.Select(convert).ToArray());
        }
    }
}
