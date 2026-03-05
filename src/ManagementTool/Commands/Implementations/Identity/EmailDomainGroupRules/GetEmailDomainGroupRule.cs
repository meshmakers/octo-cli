using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.EmailDomainGroupRules;

internal class GetEmailDomainGroupRule : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _id;

    public GetEmailDomainGroupRule(ILogger<GetEmailDomainGroupRule> logger, IOptions<OctoToolOptions> options,
        IConsoleService consoleService,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "GetEmailDomainGroupRule",
            "Gets an email domain group rule by ID.", options, identityServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _id = CommandArgumentValue.AddArgument("id", "identifier",
            ["ID of the email domain group rule"], true, 1);
    }

    public override async Task Execute()
    {
        var rtId = CommandArgumentValue.GetArgumentScalarValue<OctoObjectId>(_id);

        Logger.LogInformation("Getting email domain group rule '{RtId}' from '{ServiceClientServiceUri}'",
            rtId, ServiceClient.ServiceUri);

        var result = await ServiceClient.GetEmailDomainGroupRule(rtId);
        var resultString = JsonConvert.SerializeObject(result, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }
}
