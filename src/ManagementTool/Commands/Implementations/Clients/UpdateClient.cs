using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Clients;

internal class UpdateClient : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _clientId;
    private readonly IArgument _clientName;
    private readonly IArgument _clientUri;
    private readonly IArgument _redirectUri;

    public UpdateClient(ILogger<UpdateClient> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "UpdateClient", "Updates a new client.", options, identityServicesClient, authenticationService)
    {
        _clientUri = CommandArgumentValue.AddArgument("u", "clientUri", new[] { "URI of client" }, false, 1);
        _redirectUri = CommandArgumentValue.AddArgument("ru", "redirectUri",
            new[] { "Redirect URI for login procedure" }, false, 1);
        _clientId = CommandArgumentValue.AddArgument("id", "clientId", new[] { "ServiceClient ID, must be unique" },
            false, 1);
        _clientName =
            CommandArgumentValue.AddArgument("n", "name", new[] { "Display name of client used for grants" }, false, 1);
    }

    public override async Task Execute()
    {
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientId);

        Logger.LogInformation("Updating client \'{ClientId}\' at \'{ServiceClientServiceUri}\'", clientId,
            ServiceClient.ServiceUri);

        var clientDto = await ServiceClient.GetClient(clientId);

        if (CommandArgumentValue.IsArgumentUsed(_clientUri))
        {
            var clientUri = CommandArgumentValue.GetArgumentScalarValue<string>(_clientUri);

            clientDto.ClientUri = clientUri;
            clientDto.AllowedCorsOrigins = new[] { clientUri.TrimEnd('/') };
            clientDto.PostLogoutRedirectUris = new[] { clientUri.EnsureEndsWith("/") };
            clientDto.RedirectUris = new[] { clientUri.EnsureEndsWith("/") };
        }

        if (CommandArgumentValue.IsArgumentUsed(_redirectUri))
        {
            var redirectUri = CommandArgumentValue.GetArgumentScalarValue<string>(_redirectUri);

            clientDto.RedirectUris = new[] { redirectUri };
        }

        if (CommandArgumentValue.IsArgumentUsed(_clientName))
        {
            var clientName = CommandArgumentValue.GetArgumentScalarValue<string>(_clientName);

            clientDto.ClientName = clientName;
        }

        await ServiceClient.UpdateClient(clientId, clientDto);

        Logger.LogInformation("ServiceClient \'{ClientId}\' at \'{ServiceClientServiceUri}\' updated", clientId,
            ServiceClient.ServiceUri);
    }
}
