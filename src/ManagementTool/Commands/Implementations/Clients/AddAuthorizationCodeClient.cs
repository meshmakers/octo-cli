using IdentityModel;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared;
using Meshmakers.Octo.Communication.Contracts;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Clients;

internal class AddAuthorizationCodeClient : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _clientId;
    private readonly IArgument _clientName;
    private readonly IArgument _clientUri;
    private readonly IArgument _redirectUri;

    public AddAuthorizationCodeClient(ILogger<AddAuthorizationCodeClient> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "AddAuthorizationCodeClient", "Adds a new client using grant type 'AuthorizationCode'.", options,
            identityServicesClient, authenticationService)
    {
        _clientUri = CommandArgumentValue.AddArgument("u", "clientUri", ["URI of client"],
            true, 1);
        _redirectUri = CommandArgumentValue.AddArgument("ru", "redirectUri",
            ["Redirect URI for login procedure"], false, 1);
        _clientId = CommandArgumentValue.AddArgument("id", "clientId", ["ServiceClient ID, must be unique"],
            true,
            1);
        _clientName = CommandArgumentValue.AddArgument("n", "name", ["Display name of client used for grants"],
            true,
            1);
    }

    public override async Task Execute()
    {
        var clientUri = CommandArgumentValue.GetArgumentScalarValue<string>(_clientUri);
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientId);
        var clientName = CommandArgumentValue.GetArgumentScalarValue<string>(_clientName);

        Logger.LogInformation("Creating client \'{ClientId}\' (URI \'{ClientUri}\') at \'{ServiceClientServiceUri}\'",
            clientId, clientUri, ServiceClient.ServiceUri);

        var clientDto = new ClientDto
        {
            IsEnabled = true,
            ClientId = clientId,
            ClientName = clientName,
            ClientUri = clientUri,
            AllowedCorsOrigins = new[] { clientUri.TrimEnd('/') },
            AllowedGrantTypes = new[] { OidcConstants.GrantTypes.AuthorizationCode },
            AllowedScopes = new[] { CommonConstants.SystemApiFullAccess },
            PostLogoutRedirectUris = new[] { clientUri.EnsureEndsWith("/") },
            RedirectUris = new[] { clientUri.EnsureEndsWith("/") },
            IsOfflineAccessEnabled = true
        };

        if (CommandArgumentValue.IsArgumentUsed(_redirectUri))
        {
            var redirectUri = CommandArgumentValue.GetArgumentScalarValue<string>(_redirectUri);

            clientDto.RedirectUris = new[] { redirectUri };
        }

        await ServiceClient.CreateClient(clientDto);

        Logger.LogInformation(
            "ServiceClient \'{ClientId}\' (URI \'{ClientUri}\') at \'{ServiceClientServiceUri}\' created", clientId,
            clientUri, ServiceClient.ServiceUri);
    }
}
