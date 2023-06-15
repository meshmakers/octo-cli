using System;
using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.ApiSecrets;

internal class UpdateApiSecretClient : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _clientIdArg;
    private readonly IArgument _descriptionArg;
    private readonly IArgument _expirationArg;
    private readonly IArgument _secretValueArg;

    public UpdateApiSecretClient(ILogger<UpdateApiSecretClient> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "UpdateApiSecretClient", "Updates an API secret for a client.", options, identityServicesClient,
            authenticationService)
    {
        _clientIdArg = CommandArgumentValue.AddArgument("cid", "clientId", new[] { "ID of client" },
            true,
            1);
        _secretValueArg = CommandArgumentValue.AddArgument("s", "secretValue", new[] { "Value sha256 encoded of the secret" },
            true,
            1);
        _expirationArg =
            CommandArgumentValue.AddArgument("e", "expirationDate", new[] { "Expiration date of secret" }, false, 1);
        _descriptionArg =
            CommandArgumentValue.AddArgument("d", "description", new[] { "Description of scope scope" }, false, 1);
    }

    public override async Task Execute()
    {
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientIdArg);
        var secretValue = CommandArgumentValue.GetArgumentScalarValue<string>(_secretValueArg);

        Logger.LogInformation("Updating API secret \'{SecretValue}\' for client \'{ClientId}\' at \'{ServiceClientServiceUri}\'",
            secretValue, clientId,
            ServiceClient.ServiceUri);

        var secretDto = await ServiceClient.GetApiSecretForClient(clientId, secretValue);
        if (secretDto == null)
        {
            Logger.LogError("API secret \'{SecretValue}\' for client \'{ClientId}\' at \'{ServiceClientServiceUri}\' not found",
                secretValue, clientId,
                ServiceClient.ServiceUri);
            return;
        }

        if (CommandArgumentValue.IsArgumentUsed(_expirationArg))
        {
            secretDto.ExpirationDate = CommandArgumentValue.GetArgumentScalarValue<DateTime?>(_expirationArg);
        }

        if (CommandArgumentValue.IsArgumentUsed(_descriptionArg))
        {
            secretDto.Description = CommandArgumentValue.GetArgumentScalarValue<string>(_descriptionArg);
        }

        await ServiceClient.UpdateApiSecretClient(clientId, secretDto);

        Logger.LogInformation("API secret \'{SecretValue}\' for client \'{ClientId}\' at \'{ServiceClientServiceUri}\' updated",
            secretValue, clientId,
            ServiceClient.ServiceUri);
    }
}
