using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.ApiSecrets;

internal class DeleteApiSecretClient : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _clientIdArg;
    private readonly IArgument _secretValueArg;
    private readonly IArgument _yesArg;

    public DeleteApiSecretClient(ILogger<DeleteApiSecretClient> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.IdentityServicesGroup, "DeleteApiSecretClient", "Deletes a secret of a client.",
            options,
            identityServicesClient, authenticationService)
    {
        _confirmationService = confirmationService;

        _clientIdArg = CommandArgumentValue.AddArgument("cid", "clientId", ["ID of client"],
            true,
            1);
        _secretValueArg = CommandArgumentValue.AddArgument("s", "secretValue", ["Value (sha256) of secret"],
            true,
            1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override async Task Execute()
    {
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientIdArg);
        var secretValue = CommandArgumentValue.GetArgumentScalarValue<string>(_secretValueArg);

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm($"Are you sure you want to delete API secret for client '{clientId}'?"))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation(
            "Deleting API secret \'{SecretValue}\' for client \'{ClientId}\' from \'{ServiceClientServiceUri}\'",
            secretValue, clientId,
            ServiceClient.ServiceUri);

        await ServiceClient.DeleteApiSecretClient(clientId, secretValue);

        Logger.LogInformation("API API secret \'{SecretValue}\' for client \'{ClientId}\' deleted", secretValue,
            clientId);
    }
}
