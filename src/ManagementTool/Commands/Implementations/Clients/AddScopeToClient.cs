using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Clients;

public class AddScopeToClient : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _clientId;
    private readonly IArgument _scopeName;

    public AddScopeToClient(ILogger<AddScopeToClient> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "AddScopeToClient", "Grants the access to a client for a scope .", options,
            identityServicesClient, authenticationService)
    {
        _clientId = CommandArgumentValue.AddArgument("id", "clientId", ["ServiceClient ID, must be unique"],
            true,
            1);
        _scopeName = CommandArgumentValue.AddArgument("n", "name", ["Scope name"],
            true,
            1);
    }

    public override async Task Execute()
    {
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientId);
        var scopeName = CommandArgumentValue.GetArgumentScalarValue<string>(_scopeName);

        Logger.LogInformation("Allowing scope \'{ScopeName}\' for client \'{ClientId}\' at \'{ServiceClientServiceUri}\'",
            scopeName, clientId, ServiceClient.ServiceUri);

        var client = await ServiceClient.GetClient(clientId);

        var clientDto = new ClientDto
        {
            AllowedScopes = (client.AllowedScopes ?? new List<string>()).Append(scopeName)
        };

        await ServiceClient.UpdateClient(clientId, clientDto);

        Logger.LogInformation(
            "ServiceClient \'{ClientId}\' at \'{ServiceClientServiceUri}\' updated", clientId,
            ServiceClient.ServiceUri);
    }
}
