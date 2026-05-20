using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

internal class ProvisionClientInExistingTenants : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _clientId;

    public ProvisionClientInExistingTenants(ILogger<ProvisionClientInExistingTenants> logger,
        IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "ProvisionClientInExistingTenants",
            "Backfill: provisions a flagged ClientCredentials client into every existing sub-tenant of the active context tenant. Idempotent.",
            options, identityServicesClient, authenticationService)
    {
        _clientId = CommandArgumentValue.AddArgument("id", "clientId",
            ["The ClientId to backfill into existing sub-tenants. Must already have AutoProvisionInChildTenants enabled."],
            true, 1);
    }

    public override async Task Execute()
    {
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientId);

        Logger.LogInformation("Backfilling client '{ClientId}' into existing sub-tenants at '{ServiceClientServiceUri}'",
            clientId, ServiceClient.ServiceUri);

        var result = await ServiceClient.ProvisionClientInExistingTenants(clientId);

        Logger.LogInformation(
            "Backfill complete: {Considered} sub-tenant(s) considered, {Newly} newly provisioned, {Already} already present",
            result.ChildTenantsConsidered, result.NewlyProvisioned, result.AlreadyPresent);
    }
}
