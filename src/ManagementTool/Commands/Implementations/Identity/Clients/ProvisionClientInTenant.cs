using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

internal class ProvisionClientInTenant : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _clientId;
    private readonly IArgument _childTenantId;

    public ProvisionClientInTenant(ILogger<ProvisionClientInTenant> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "ProvisionClientInTenant",
            "Manually provisions a flagged ClientCredentials client into one specific sub-tenant.",
            options, identityServicesClient, authenticationService)
    {
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

        var result = await ServiceClient.ProvisionClientInTenant(clientId, childTenantId);

        Logger.LogInformation(
            "Provision complete: {Considered} flagged client(s), {Newly} newly provisioned, {Already} already present",
            result.FlaggedClientsConsidered, result.NewlyProvisioned, result.AlreadyPresent);
    }
}
