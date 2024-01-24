using System;
using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.ApiSecrets;

internal class UpdateApiSecretApiResource : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _descriptionArg;
    private readonly IArgument _expirationArg;
    private readonly IArgument _nameArg;
    private readonly IArgument _secretValueArg;

    public UpdateApiSecretApiResource(ILogger<UpdateApiSecretApiResource> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "UpdateApiSecretApiResource", "Updates an API secret for an API resource.", options, identityServicesClient,
            authenticationService)
    {
        _nameArg = CommandArgumentValue.AddArgument("n", "name", ["Name of API resource"],
            true,
            1);
        _secretValueArg = CommandArgumentValue.AddArgument("s", "secretValue", ["Value sha256 encoded of the secret"],
            true,
            1);
        _expirationArg =
            CommandArgumentValue.AddArgument("e", "expirationDate", ["Expiration date of secret"], false, 1);
        _descriptionArg =
            CommandArgumentValue.AddArgument("d", "description", ["Description of scope scope"], false, 1);
    }

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg);
        var secretValue = CommandArgumentValue.GetArgumentScalarValue<string>(_secretValueArg);

        Logger.LogInformation("Updating API secret \'{SecretValue}\' for API resource \'{Name}\' at \'{ServiceClientServiceUri}\'",
            secretValue, name,
            ServiceClient.ServiceUri);

        var secretDto = await ServiceClient.GetApiSecretForApiResource(name, secretValue);
        if (secretDto == null)
        {
            Logger.LogError("API secret \'{SecretValue}\' for API resource \'{Name}\' at \'{ServiceClientServiceUri}\' not found",
                secretValue, name,
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

        await ServiceClient.UpdateApiSecretApiResource(name, secretDto);

        Logger.LogInformation("API secret \'{SecretValue}\' for API resource \'{Name}\' at \'{ServiceClientServiceUri}\' updated",
            secretValue, name,
            ServiceClient.ServiceUri);
    }
}
