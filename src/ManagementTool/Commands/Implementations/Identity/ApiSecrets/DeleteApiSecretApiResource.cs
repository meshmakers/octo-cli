using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.ApiSecrets;

internal class DeleteApiSecretApiResource : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _nameArg;
    private readonly IArgument _secretValueArg;
    private readonly IArgument _yesArg;

    public DeleteApiSecretApiResource(ILogger<DeleteApiSecretApiResource> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.IdentityServicesGroup, "DeleteApiSecretApiResource",
            "Deletes a secret of an API resource.", options, identityServicesClient,
            authenticationService)
    {
        _confirmationService = confirmationService;

        _nameArg = CommandArgumentValue.AddArgument("n", "name", ["Name of API resource"],
            true,
            1);
        _secretValueArg = CommandArgumentValue.AddArgument("s", "secretValue", ["Value (sha256) of secret"],
            true,
            1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg);
        var secretValue = CommandArgumentValue.GetArgumentScalarValue<string>(_secretValueArg);

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm($"Are you sure you want to delete API secret for resource '{name}'?"))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation(
            "Deleting API secret \'{SecretValue}\' for client \'{Name}\' from \'{ServiceClientServiceUri}\'",
            secretValue,
            name,
            ServiceClient.ServiceUri);

        await ServiceClient.DeleteApiSecretApiResource(name, secretValue);

        Logger.LogInformation("API API secret \'{SecretValue}\' for client \'{Name}\' deleted", secretValue, name);
    }
}
