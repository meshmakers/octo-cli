using System.Linq;
using System.Threading.Tasks;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.ApiScopes;

internal class GetApiScopes : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConsoleService _consoleService;

    public GetApiScopes(ILogger<GetApiScopes> logger, IOptions<OctoToolOptions> options,
        IConsoleService consoleService,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "GetApiScopes", "Gets all api scopes.", options, identityServicesClient, authenticationService)
    {
        _consoleService = consoleService;
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Getting API scopes from \'{ServiceClientServiceUri}\'", ServiceClient.ServiceUri);

        var result = await ServiceClient.GetApiScopes();
        if (!result.Any())
        {
            Logger.LogInformation("No API scopes has been returned");
            return;
        }

        var resultString = JsonConvert.SerializeObject(result, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }
}
