using System.Threading.Tasks;
using IdentityModel;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Common.Shared;
using Meshmakers.Octo.Common.Shared.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Clients;

internal class AddDeviceCodeClient : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _clientId;
    private readonly IArgument _clientName;
    private readonly IArgument _clientSecret;

    public AddDeviceCodeClient(ILogger<AddDeviceCodeClient> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "AddDeviceCodeClient", "Adds a new client using grant type 'device code'.", options,
            identityServicesClient, authenticationService)
    {
        _clientId = CommandArgumentValue.AddArgument("id", "clientId", new[] { "ServiceClient ID, must be unique" },
            true,
            1);
        _clientName = CommandArgumentValue.AddArgument("n", "name", new[] { "Display name of client used for grants" },
            true,
            1);
        _clientSecret = CommandArgumentValue.AddArgument("s", "secret",
            new[] { "ApiSecret that is used for client credential authentication" }, true,
            1);
    }

    public override async Task Execute()
    {
        var clientSecret = CommandArgumentValue.GetArgumentScalarValue<string>(_clientSecret);
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientId);
        var clientName = CommandArgumentValue.GetArgumentScalarValue<string>(_clientName);

        Logger.LogInformation("Creating client \'{ClientId}\' at \'{ServiceClientServiceUri}\'", clientId,
            ServiceClient.ServiceUri);

        var clientDto = new ClientDto
        {
            IsEnabled = true,
            ClientId = clientId,
            ClientName = clientName,
            ClientSecret = clientSecret,
            AllowedGrantTypes = new[] { OidcConstants.GrantTypes.DeviceCode },
            AllowedScopes = new[] { CommonConstants.SystemApiFullAccess },
            IsOfflineAccessEnabled = true
        };

        await ServiceClient.CreateClient(clientDto);

        Logger.LogInformation("ServiceClient \'{ClientId}\' at \'{ServiceClientServiceUri}\' created", clientId,
            ServiceClient.ServiceUri);
    }
}
