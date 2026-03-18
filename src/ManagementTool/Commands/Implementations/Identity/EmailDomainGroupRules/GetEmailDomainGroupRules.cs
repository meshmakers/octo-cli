using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.EmailDomainGroupRules;

internal class GetEmailDomainGroupRules : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConsoleService _consoleService;

    public GetEmailDomainGroupRules(ILogger<GetEmailDomainGroupRules> logger, IOptions<OctoToolOptions> options,
        IConsoleService consoleService,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "GetEmailDomainGroupRules",
            "Gets all email domain group rules.", options, identityServicesClient, authenticationService)
    {
        _consoleService = consoleService;
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Getting email domain group rules from '{ServiceClientServiceUri}'",
            ServiceClient.ServiceUri);

        var result = await ServiceClient.GetEmailDomainGroupRules();
        if (!result.Any())
        {
            Logger.LogInformation("No email domain group rules have been returned");
            return;
        }

        var resultString = JsonConvert.SerializeObject(result, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }
}
