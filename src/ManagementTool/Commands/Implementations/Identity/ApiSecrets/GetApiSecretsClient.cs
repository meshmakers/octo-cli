using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.ApiSecrets;

internal class GetApiSecretsClient : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _clientIdArg;
    private readonly IConsoleService _consoleService;

    public GetApiSecretsClient(ILogger<GetApiSecretsClient> logger, IOptions<OctoToolOptions> options,
        IConsoleService consoleService,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "GetApiSecretsClient", "Gets all secrets of a client.", options,
            identityServicesClient, authenticationService)
    {
        _consoleService = consoleService;

        _clientIdArg = CommandArgumentValue.AddArgument("cid", "clientId", ["ID of client"],
            true,
            1);
    }

    public override async Task Execute()
    {
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientIdArg);

        Logger.LogInformation("Getting API secrets for client \'{ClientId}\' from \'{ServiceClientServiceUri}\'",
            clientId,
            ServiceClient.ServiceUri);

        var result = await ServiceClient.GetApiSecretsForClient(clientId);
        if (!result.Any())
        {
            Logger.LogInformation("No API secrets has been returned");
            return;
        }

        var resultString = JsonConvert.SerializeObject(result, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }
}