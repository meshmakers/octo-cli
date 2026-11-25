using System.Text.Json.Nodes;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Knowledge;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Ai.Agents;

/// <summary>
///     Lists the agents of the tenant (MCP tool <c>list_agents</c>) or shows one agent in detail (<c>get_agent</c>, with
///     <c>-a</c>), with the signed-in user's token. Changes nothing.
/// </summary>
internal class GetAgentsCommand : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IArgument _agentArg;
    private readonly IOptions<OctoToolAuthenticationOptions> _authenticationOptions;
    private readonly IConsoleService _consoleService;
    private readonly IArgument _jsonArg;
    private readonly IMcpToolClient _mcpToolClient;
    private readonly IArgument _tenantIdArg;
    private readonly ITenantTokenProvider? _tenantTokenProvider;

    public GetAgentsCommand(ILogger<GetAgentsCommand> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IAuthenticationService authenticationService,
        IOptions<OctoToolAuthenticationOptions> authenticationOptions, IMcpToolClient mcpToolClient,
        IConsoleService consoleService, ITenantTokenProvider? tenantTokenProvider = null)
        : base(logger, Constants.AiServicesGroup, "GetAgents",
            "Lists the agents of the tenant with their mandates, or shows one agent with -a (tenant administrators " +
            "only). Calls the MCP tools list_agents / get_agent with your token; changes nothing.",
            options, assetServicesClient, authenticationService)
    {
        _authenticationOptions = authenticationOptions;
        _mcpToolClient = mcpToolClient;
        _consoleService = consoleService;
        _tenantTokenProvider = tenantTokenProvider;

        _agentArg = CommandArgumentValue.AddArgument("a", "agent",
            ["Runtime id or name of one agent to show in detail. Default: list every agent"], false, 1);
        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId",
            ["Id of the tenant. Default: the tenant of the context"], false, 1);
        _jsonArg = CommandArgumentValue.AddArgument("json", "json",
            ["Print the answer of the MCP tool as JSON"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [], description: "List the agents of the tenant of the context"),
                new CodeSample(arguments: [new CodeSampleArgument(_agentArg, "Knowledge curator")],
                    description: "Show one agent with its grants and whether its service-account client is enabled")
            ],
            Notes:
            [
                "Tenant administrators only (role TenantManagement or UserManagement); an agent's own token is refused.",
                "Calls <McpServiceUrl of the context>/<tenant>/mcp (set it once with AddContext -msu or Config -msu)."
            ]);

    public override async Task Execute()
    {
        var tenantId = AgentMcp.TenantId(CommandArgumentValue, _tenantIdArg, Options.Value);
        var json = CommandArgumentValue.IsArgumentUsed(_jsonArg);

        if (AgentMcp.Text(CommandArgumentValue, _agentArg) is { } agent)
        {
            var one = await AgentMcp.CallAsync(_mcpToolClient, Options.Value, _authenticationOptions.Value.AccessToken,
                _tenantTokenProvider, tenantId, AgentMcp.GetTool, new JsonObject { ["agent"] = agent }, Logger);
            AgentMcp.PrintAgentAnswer(_consoleService, one, json, $"Agent of tenant '{tenantId}':");
            return;
        }

        var answer = await AgentMcp.CallAsync(_mcpToolClient, Options.Value, _authenticationOptions.Value.AccessToken,
            _tenantTokenProvider, tenantId, AgentMcp.ListTool, new JsonObject(), Logger);
        if (json)
        {
            _consoleService.WriteLine(answer.ToJsonString());
            return;
        }

        var agents = KnowledgePlatformUpdatesMcp.Items(answer, "agents").ToList();
        _consoleService.WriteLine($"Tenant '{tenantId}': {agents.Count} agent(s).");
        foreach (var item in agents)
        {
            AgentMcp.PrintAgent(_consoleService, item, detailed: false);
        }
    }
}
