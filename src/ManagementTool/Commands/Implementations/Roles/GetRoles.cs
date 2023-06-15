using System.Linq;
using System.Threading.Tasks;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Roles;

internal class GetRoles : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConsoleService _consoleService;

    public GetRoles(ILogger<GetRoles> logger,
        IConsoleService consoleService,
        IOptions<OctoToolOptions> options, IIdentityServicesClient identityServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, "GetRoles", "Gets roles.", options, identityServicesClient, authenticationService)
    {
        _consoleService = consoleService;
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Getting roles from \'{ServiceClientServiceUri}\'", ServiceClient.ServiceUri);

        var result = await ServiceClient.GetRoles();

        var users = result.ToArray();
        if (!users.Any())
        {
            Logger.LogInformation("No roles has been returned");
            return;
        }

        var resultString = JsonConvert.SerializeObject(result, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }
}
