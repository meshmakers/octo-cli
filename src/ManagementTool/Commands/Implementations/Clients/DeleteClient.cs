using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Clients;

internal class DeleteClient : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _clientId;

    public DeleteClient(ILogger<DeleteClient> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "DeleteClient", "Deletes a client.", options, identityServicesClient, authenticationService)
    {
        _clientId = CommandArgumentValue.AddArgument("id", "clientId", new[] { "ServiceClient ID, must be unique" },
            true,
            1);
    }

    public override async Task Execute()
    {
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientId);

        Logger.LogInformation("Deleting client \'{ClientId}\' from \'{ServiceClientServiceUri}\'", clientId,
            ServiceClient.ServiceUri);

        await ServiceClient.DeleteClient(clientId);

        Logger.LogInformation("ServiceClient \'{ClientId}\' deleted", clientId);
    }
}
