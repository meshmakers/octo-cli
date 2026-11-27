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
///     Disables an agent through the MCP tool <c>set_agent_enabled</c> (enabled: false), with the signed-in user's token.
/// </summary>
internal class DisableAgentCommand : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IArgument _agentArg;
    private readonly IOptions<OctoToolAuthenticationOptions> _authenticationOptions;
    private readonly IConsoleService _consoleService;
    private readonly IArgument _jsonArg;
    private readonly IMcpToolClient _mcpToolClient;
    private readonly IArgument _tenantIdArg;
    private readonly ITenantTokenProvider? _tenantTokenProvider;

    public DisableAgentCommand(ILogger<DisableAgentCommand> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IAuthenticationService authenticationService,
        IOptions<OctoToolAuthenticationOptions> authenticationOptions, IMcpToolClient mcpToolClient,
        IConsoleService consoleService, ITenantTokenProvider? tenantTokenProvider = null)
        : base(logger, Constants.AiServicesGroup, "DisableAgent",
            "Disables an agent of the tenant (kill switch): it starts no run and its service-account client is disabled (tenant administrators only). Calls the MCP tool set_agent_enabled with your token.",
            options, assetServicesClient, authenticationService)
    {
        _authenticationOptions = authenticationOptions;
        _mcpToolClient = mcpToolClient;
        _consoleService = consoleService;
        _tenantTokenProvider = tenantTokenProvider;

        _agentArg = CommandArgumentValue.AddArgument("a", "agent", ["Runtime id or name of the agent"], true, 1);
        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId",
            ["Id of the tenant. Default: the tenant of the context"], false, 1);
        _jsonArg = CommandArgumentValue.AddArgument("json", "json",
            ["Print the answer of the MCP tool as JSON"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_agentArg, "Knowledge curator")],
                    description: "Disable the agent 'Knowledge curator' of the tenant of the context")
            ],
            Notes:
            [
                "Tenant administrators only (role TenantManagement or UserManagement); an agent's own token is refused.",
                "Calls <McpServiceUrl of the context>/<tenant>/mcp (set it once with AddContext -msu or Config -msu)."
            ]);

    public override async Task Execute()
    {
        var tenantId = AgentMcp.TenantId(CommandArgumentValue, _tenantIdArg, Options.Value);
        var arguments = new JsonObject
        {
            ["agent"] = CommandArgumentValue.GetArgumentScalarValue<string>(_agentArg),
            ["enabled"] = false
        };

        var answer = await AgentMcp.CallAsync(_mcpToolClient, Options.Value, _authenticationOptions.Value.AccessToken,
            _tenantTokenProvider, tenantId, AgentMcp.SetEnabledTool, arguments, Logger);
        AgentMcp.PrintAgentAnswer(_consoleService, answer, CommandArgumentValue.IsArgumentUsed(_jsonArg),
            $"Agent disabled in tenant '{tenantId}':");
    }
}
