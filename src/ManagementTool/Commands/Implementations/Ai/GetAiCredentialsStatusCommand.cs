using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AiServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Ai;

/// <summary>
///     Reads the current AI credential-lease snapshot for the active tenant. Token
///     ciphertext is never returned — only status + expiries — so an operator who can
///     run this command still cannot extract the underlying Anthropic subscription.
///     A <c>NoLease</c> status is the initial pre-register state and not an error.
/// </summary>
internal class GetAiCredentialsStatusCommand : ServiceClientOctoCommand<IAiServicesClient>
{
    public GetAiCredentialsStatusCommand(ILogger<GetAiCredentialsStatusCommand> logger,
        IOptions<OctoToolOptions> options,
        IAiServicesClient aiServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.AiServicesGroup, "GetAiCredentialsStatus",
            "Returns the AI credential-lease status (expiries + generation) for the active tenant. Token plaintext is never disclosed.",
            options, aiServicesClient, authenticationService)
    {
    }

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        Logger.LogInformation(
            "Reading AI credential-lease status for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            Options.Value.TenantId, ServiceClient.ServiceUri);

        var status = await ServiceClient.GetCredentialsStatusAsync();

        Logger.LogInformation(
            "Lease status={Status}, generation={Generation}, access exp={AccessExpiresAt}, refresh exp={RefreshExpiresAt}",
            status.Status, status.Generation, status.AccessExpiresAt, status.RefreshExpiresAt);
    }
}
