using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

internal class SetClientAutoProvision : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _clientId;
    private readonly IArgument _enabled;

    public SetClientAutoProvision(ILogger<SetClientAutoProvision> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "SetClientAutoProvision",
            "Flips the AutoProvisionInChildTenants flag on an existing ClientCredentials client. Flipping to true does not auto-backfill — use ProvisionClientInExistingTenants for that.",
            options, identityServicesClient, authenticationService)
    {
        _clientId = CommandArgumentValue.AddArgument("id", "clientId",
            ["The ClientId on which to set the flag"], true, 1);
        _enabled = CommandArgumentValue.AddArgument("e", "enabled",
            ["true to enable auto-provisioning into child tenants, false to disable"],
            true, 1);
    }

    public override async Task Execute()
    {
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientId);
        var enabled = CommandArgumentValue.GetArgumentScalarValue<bool>(_enabled);

        Logger.LogInformation(
            "Setting AutoProvisionInChildTenants={Enabled} for client '{ClientId}' at '{ServiceClientServiceUri}'",
            enabled, clientId, ServiceClient.ServiceUri);

        await ServiceClient.SetClientAutoProvisionInChildTenants(clientId, enabled);

        Logger.LogInformation("AutoProvisionInChildTenants for client '{ClientId}' now {State}",
            clientId, enabled ? "enabled" : "disabled");
    }
}
