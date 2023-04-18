using System;
using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Common.Shared.DataTransferObjects;
using Meshmakers.Octo.Frontend.Client.System;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.ApiSecrets;

internal class CreateApiSecretClient : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _clientIdArg;
    private readonly IArgument _expirationArg;
    private readonly IArgument _descriptionArg;
    
    public CreateApiSecretClient(ILogger<CreateApiSecretClient> logger, IOptions<OctoToolOptions> options,
        IConsoleService consoleService,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "CreateApiSecretClient", "Adds new API secret for a client.", options,
            identityServicesClient, authenticationService)
    {
        _consoleService = consoleService;

        _clientIdArg = CommandArgumentValue.AddArgument("cid", "clientId", new[] { "ID of client" },
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

        Logger.LogInformation("Creating API secret for client \'{ClientId}\' at \'{ServiceClientServiceUri}\'", clientId,
            ServiceClient.ServiceUri);

        var apiSecretDto = new ApiSecretDto
        {
            ExpirationDate = CommandArgumentValue.GetArgumentScalarValueOrDefault<DateTime?>(_expirationArg),
            Description = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_descriptionArg),
        };

        ApiSecretDto resultSecret = await ServiceClient.CreateApiSecretForClient(clientId, apiSecretDto);

        Logger.LogInformation("API secret \'{ValueEncrypted}\' for client  \'{ClientId}\' at \'{ServiceClientServiceUri}\' created",
            resultSecret.ValueEncrypted, clientId,
            ServiceClient.ServiceUri);
        
        var resultString = JsonConvert.SerializeObject(resultSecret, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }
}
