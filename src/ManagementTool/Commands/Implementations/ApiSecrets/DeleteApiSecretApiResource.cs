using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.ApiSecrets;

internal class DeleteApiSecretApiResource : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _nameArg;
    private readonly IArgument _secretValueArg;

    public DeleteApiSecretApiResource(ILogger<DeleteApiSecretApiResource> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "DeleteApiSecretApiResource", "Deletes a secret of an API resource.", options, identityServicesClient,
            authenticationService)
    {
        _nameArg = CommandArgumentValue.AddArgument("n", "name", new[] { "Name of API resource" },
            true,
            1);
        _secretValueArg = CommandArgumentValue.AddArgument("s", "secretValue", new[] { "Value (sha256) of secret" },
            true,
            1);
    }

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg);
        var secretValue = CommandArgumentValue.GetArgumentScalarValue<string>(_secretValueArg);

        Logger.LogInformation("Deleting API secret \'{SecretValue}\' for client \'{Name}\' from \'{ServiceClientServiceUri}\'", secretValue,
            name,
            ServiceClient.ServiceUri);

        await ServiceClient.DeleteApiSecretApiResource(name, secretValue);

        Logger.LogInformation("API API secret \'{SecretValue}\' for client \'{Name}\' deleted", secretValue, name);
    }
}
