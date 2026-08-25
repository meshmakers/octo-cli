using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AiServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Ai;

internal class DisableAiCommand : ServiceClientOctoCommand<IAiServicesClient>
{
    public DisableAiCommand(ILogger<DisableAiCommand> logger, IOptions<OctoToolOptions> options,
        IAiServicesClient aiServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.AiServicesGroup, "DisableAi",
            "Disables the AI Adapter for the current tenant. The seeded AgentConfig and CK model are not removed; re-enabling is idempotent.",
            options, aiServicesClient, authenticationService)
    {
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [], description: "Basic usage"),
            ],
            Notes:
            [
                "Disabling only removes the enabled flag: the System.Ai CK model, the seeded AgentConfig / QuotaLimit " +
                "and all session, lease and audit data stay in the tenant. New AI sessions are refused until EnableAi " +
                "(which requires Communication to be enabled on the tenant).",
                "Disabling AI Services is a precondition for Delete and Detach of the tenant (AB#4255). It has no " +
                "precondition of its own - the AI service owns no deployed resources. Like the other disable commands " +
                "it acts on the tenant of the active context (UseContext or --context <name>).",
                "Reversible with EnableAi.",
            ]
        );

    public override async Task Execute()
    {
        Logger.LogInformation("Disabling AI Adapter for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            Options.Value.TenantId,
            ServiceClient.ServiceUri);

        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        await ServiceClient.DisableAsync(Options.Value.TenantId);

        Logger.LogInformation("AI Adapter for tenant '{TenantId}' at '{ServiceClientServiceUri}' disabled",
            Options.Value.TenantId,
            ServiceClient.ServiceUri);
    }
}
