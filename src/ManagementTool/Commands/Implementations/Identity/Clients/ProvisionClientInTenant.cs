using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

internal class ProvisionClientInTenant : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IdentityMethodCall _methods;
    private readonly IArgument _clientId;
    private readonly IArgument _childTenantId;

    public ProvisionClientInTenant(ILogger<ProvisionClientInTenant> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "ProvisionClientInTenant",
            "Manually provisions a flagged ClientCredentials client into one specific sub-tenant.",
            options, identityServicesClient, authenticationService)
    {
        _methods = new IdentityMethodCall(tenantClient, authenticationService, logger);
        _clientId = CommandArgumentValue.AddArgument("id", "clientId",
            ["The ClientId to provision. Must already have AutoProvisionInChildTenants enabled."],
            true, 1);
        _childTenantId = CommandArgumentValue.AddArgument("ctid", "child-tenant-id",
            ["ID of the child tenant to provision the client into"],
            true, 1);
    }

    public override async Task Execute()
    {
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientId);
        var childTenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_childTenantId);

        Logger.LogInformation(
            "Provisioning client '{ClientId}' into child tenant '{ChildTenantId}' at '{ServiceClientServiceUri}'",
            clientId, childTenantId, ServiceClient.ServiceUri);

        var value = await _methods.RunAsync(RtClientMethodClient.ProvisionMirrorInTenantMethodId,
            async () => await new RtClientMethodClient(_methods.TenantClient).ProvisionMirrorInTenantAsync(await IdentityMethodCall.ResolveClientRtIdAsync(ServiceClient, clientId),
                childTenantId));
        LogComplete(value?.FlaggedClientsConsidered ?? 0, value?.NewlyProvisioned ?? 0, value?.AlreadyPresent ?? 0);
    }

    private void LogComplete(long considered, long newlyProvisioned, long alreadyPresent)
    {
        Logger.LogInformation(
            "Provision complete: {Considered} flagged client(s), {Newly} newly provisioned, {Already} already present",
            considered, newlyProvisioned, alreadyPresent);
    }
}
