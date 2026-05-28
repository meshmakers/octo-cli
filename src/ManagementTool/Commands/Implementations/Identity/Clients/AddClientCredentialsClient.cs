using IdentityModel;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

internal class AddClientCredentialsClient : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _clientId;
    private readonly IArgument _clientName;
    private readonly IArgument _clientSecret;
    private readonly IArgument _autoProvision;

    public AddClientCredentialsClient(ILogger<AddClientCredentialsClient> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "AddClientCredentialsClient",
            "Adds a new client using grant type 'ClientCredentials'.", options,
            identityServicesClient, authenticationService)
    {
        _clientId = CommandArgumentValue.AddArgument("id", "clientId", ["ServiceClient ID, must be unique"],
            true,
            1);
        _clientName = CommandArgumentValue.AddArgument("n", "name", ["Display name of client used for grants"],
            true,
            1);
        _clientSecret = CommandArgumentValue.AddArgument("s", "secret",
            ["ApiSecret that is used for client credential authentication"], true,
            1);
        _autoProvision = CommandArgumentValue.AddArgument("apic", "autoProvision",
            [
                "If set, this client is auto-provisioned (mirrored) into every new sub-tenant of the calling tenant.",
                "Use for service-to-service / CI-CD identities that must reach many tenants with a single ClientId/secret pair."
            ], false, 0);
    }

    public override async Task Execute()
    {
        var clientSecret = CommandArgumentValue.GetArgumentScalarValue<string>(_clientSecret);
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientId);
        var clientName = CommandArgumentValue.GetArgumentScalarValue<string>(_clientName);
        var autoProvision = CommandArgumentValue.IsArgumentUsed(_autoProvision);

        Logger.LogInformation("Creating client \'{ClientId}\' at \'{ServiceClientServiceUri}\'", clientId,
            ServiceClient.ServiceUri);

        var clientDto = new ClientDto
        {
            IsEnabled = true,
            ClientId = clientId,
            ClientName = clientName,
            ClientSecret = clientSecret,
            RequireClientSecret = true,
            AllowedGrantTypes = [OidcConstants.GrantTypes.ClientCredentials],
            AllowedScopes = [CommonConstants.OctoApiFullAccess],
            IsOfflineAccessEnabled = true,
            AutoProvisionInChildTenants = autoProvision ? true : null
        };

        await ServiceClient.CreateClient(clientDto);

        Logger.LogInformation("ServiceClient \'{ClientId}\' at \'{ServiceClientServiceUri}\' created{AutoNote}",
            clientId, ServiceClient.ServiceUri,
            autoProvision ? " (AutoProvisionInChildTenants enabled)" : string.Empty);
    }
}