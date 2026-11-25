using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Ai.Agents;

/// <summary>
///     Lists the runs of the agents of a tenant. The run history does not exist yet (AB#8460): the command says so and
///     ends with an error, without signing in or calling a service.
/// </summary>
internal class GetAgentRunsCommand : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IArgument _agentArg;

    public GetAgentRunsCommand(ILogger<GetAgentRunsCommand> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.AiServicesGroup, "GetAgentRuns",
            "Lists the runs of the agents of the tenant. Not available yet: the run history comes with AB#8460; the " +
            "command says so and changes nothing.",
            options, assetServicesClient, authenticationService)
    {
        _agentArg = CommandArgumentValue.AddArgument("a", "agent",
            ["Runtime id or name of the agent whose runs to list. Default: every agent"], false, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_agentArg, "Knowledge curator")],
                    description: "List the runs of one agent (not available yet, AB#8460)")
            ],
            Notes:
            [
                "Not available yet: ends with 'Agent runs are not available yet' until the run history (AB#8460) exists. " +
                "It signs in to nothing and calls no service."
            ]);

    // No sign-in: the command reads nothing yet.
    public override Task PreValidate() => Task.CompletedTask;

    public override Task Execute() => throw new ToolException(AgentMcp.RunsNotAvailable);
}
