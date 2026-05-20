using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

internal class GetClientMirrors : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _clientId;

    public GetClientMirrors(ILogger<GetClientMirrors> logger, IOptions<OctoToolOptions> options,
        IConsoleService consoleService,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "GetClientMirrors",
            "Lists the sub-tenants a ClientCredentials client has been auto-provisioned into.", options,
            identityServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _clientId = CommandArgumentValue.AddArgument("id", "clientId",
            ["The ClientId whose mirror sub-tenants you want to list"], true, 1);
    }

    public override async Task Execute()
    {
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientId);

        Logger.LogInformation("Listing mirrors for client '{ClientId}' at '{ServiceClientServiceUri}'",
            clientId, ServiceClient.ServiceUri);

        var mirrors = await ServiceClient.GetClientMirrors(clientId);

        _consoleService.WriteLine(JsonConvert.SerializeObject(mirrors, Formatting.Indented));
    }
}
