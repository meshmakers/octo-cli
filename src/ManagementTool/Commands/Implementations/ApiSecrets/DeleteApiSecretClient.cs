using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.ApiSecrets;

internal class DeleteApiSecretClient : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _clientIdArg;
    private readonly IArgument _secretValueArg;

    public DeleteApiSecretClient(ILogger<DeleteApiSecretClient> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "DeleteApiSecretClient", "Deletes a secret of a client.", options, identityServicesClient, authenticationService)
    {
        _clientIdArg = CommandArgumentValue.AddArgument("cid", "clientId", ["ID of client"],
            true,
            1);
        _secretValueArg = CommandArgumentValue.AddArgument("s", "secretValue", ["Value (sha256) of secret"],
            true,
            1);
    }

    public override async Task Execute()
    {
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientIdArg);
        var secretValue = CommandArgumentValue.GetArgumentScalarValue<string>(_secretValueArg);

        Logger.LogInformation("Deleting API secret \'{SecretValue}\' for client \'{ClientId}\' from \'{ServiceClientServiceUri}\'",
            secretValue, clientId,
            ServiceClient.ServiceUri);

        await ServiceClient.DeleteApiSecretClient(clientId, secretValue);

        Logger.LogInformation("API API secret \'{SecretValue}\' for client \'{ClientId}\' deleted", secretValue, clientId);
    }
}
