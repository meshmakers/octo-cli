using System;
using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Common.Shared.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.ApiSecrets;

internal class CreateApiSecretApiResource : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _nameArg;
    private readonly IArgument _expirationArg;
    private readonly IArgument _descriptionArg;
    
    public CreateApiSecretApiResource(ILogger<CreateApiSecretApiResource> logger, IOptions<OctoToolOptions> options,
        IConsoleService consoleService,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "CreateApiSecretApiResource", "Adds new API secret for an API resource.", options,
            identityServicesClient, authenticationService)
    {
        _consoleService = consoleService;

        _nameArg = CommandArgumentValue.AddArgument("n", "name", new[] { "Name of API resource" },
            true,
            1);
        _expirationArg =
            CommandArgumentValue.AddArgument("e", "expirationDate", new[] { "Expiration date of secret" }, false, 1);
        _descriptionArg =
            CommandArgumentValue.AddArgument("d", "description", new[] { "Description of scope scope" }, false, 1);
    }

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg);

        Logger.LogInformation("Creating API secret for API resource \'{Name}\' at \'{ServiceClientServiceUri}\'", name,
            ServiceClient.ServiceUri);

        var apiSecretDto = new ApiSecretDto
        {
            ExpirationDate = CommandArgumentValue.GetArgumentScalarValueOrDefault<DateTime?>(_expirationArg),
            Description = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_descriptionArg),
        };

        ApiSecretDto resultSecret = await ServiceClient.CreateApiSecretForApiResource(name, apiSecretDto);

        Logger.LogInformation("API secret \'{ValueEncrypted}\' for client  \'{Name}\' at \'{ServiceClientServiceUri}\' created",
            resultSecret.ValueEncrypted, name,
            ServiceClient.ServiceUri);
        
        var resultString = JsonConvert.SerializeObject(resultSecret, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }
}
