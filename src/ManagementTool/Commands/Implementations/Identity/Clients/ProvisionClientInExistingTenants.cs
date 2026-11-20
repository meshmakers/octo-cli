using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

internal class ProvisionClientInExistingTenants : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IdentityMethodCall _methods;
    private readonly IArgument _clientId;

    public ProvisionClientInExistingTenants(ILogger<ProvisionClientInExistingTenants> logger,
        IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "ProvisionClientInExistingTenants",
            "Backfill: provisions a flagged ClientCredentials client into every existing sub-tenant of the active context tenant. Idempotent.",
            options, identityServicesClient, authenticationService)
    {
        _methods = new IdentityMethodCall(tenantClient, authenticationService, logger);
        _clientId = CommandArgumentValue.AddArgument("id", "clientId",
            ["The ClientId to backfill into existing sub-tenants. Must already have AutoProvisionInChildTenants enabled."],
            true, 1);
    }

    public override async Task Execute()
    {
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientId);

        Logger.LogInformation("Backfilling client '{ClientId}' into existing sub-tenants at '{ServiceClientServiceUri}'",
            clientId, ServiceClient.ServiceUri);

        var value = await _methods.RunAsync(RtClientMethodClient.ProvisionMirrorsMethodId,
            async () => await new RtClientMethodClient(_methods.TenantClient).ProvisionMirrorsAsync(await IdentityMethodCall.ResolveClientRtIdAsync(ServiceClient, clientId)));
        LogComplete(value?.ChildTenantsConsidered ?? 0, value?.NewlyProvisioned ?? 0, value?.AlreadyPresent ?? 0);
    }

    private void LogComplete(long considered, long newlyProvisioned, long alreadyPresent)
    {
        Logger.LogInformation(
            "Backfill complete: {Considered} sub-tenant(s) considered, {Newly} newly provisioned, {Already} already present",
            considered, newlyProvisioned, alreadyPresent);
    }
}
