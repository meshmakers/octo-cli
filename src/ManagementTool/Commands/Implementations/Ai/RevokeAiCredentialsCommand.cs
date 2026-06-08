using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AiServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Ai;

/// <summary>
///     Marks the active tenant's AI credential lease as <c>Revoked</c>. The ciphertext is
///     retained for audit, but new sessions refuse to start until a fresh subscription is
///     registered via the ticket-redeem flow. Destructive — gated on
///     <see cref="IConfirmationService" /> like every other destructive CLI verb.
/// </summary>
internal class RevokeAiCredentialsCommand : ServiceClientOctoCommand<IAiServicesClient>
{
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _yesArg;

    public RevokeAiCredentialsCommand(ILogger<RevokeAiCredentialsCommand> logger, IOptions<OctoToolOptions> options,
        IAiServicesClient aiServicesClient, IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.AiServicesGroup, "RevokeAiCredentials",
            "Revokes the active tenant's AI credential lease. New sessions cannot start until a fresh subscription is registered. Ciphertext is preserved for audit.",
            options, aiServicesClient, authenticationService)
    {
        _confirmationService = confirmationService;
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm(
                $"revoke AI credential lease for tenant '{Options.Value.TenantId}'? New sessions will fail until a fresh subscription is registered"))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation(
            "Revoking AI credential lease for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            Options.Value.TenantId, ServiceClient.ServiceUri);

        var status = await ServiceClient.RevokeCredentialsAsync();

        Logger.LogInformation(
            "Lease revoked: status={Status}, generation={Generation}",
            status.Status, status.Generation);
    }
}
