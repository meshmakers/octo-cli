using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.EmailDomainGroupRules;

internal class DeleteEmailDomainGroupRule : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _id;

    public DeleteEmailDomainGroupRule(ILogger<DeleteEmailDomainGroupRule> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "DeleteEmailDomainGroupRule",
            "Deletes an email domain group rule.", options, identityServicesClient, authenticationService)
    {
        _id = CommandArgumentValue.AddArgument("id", "identifier",
            ["ID of the email domain group rule"], true, 1);
    }

    public override async Task Execute()
    {
        var rtId = CommandArgumentValue.GetArgumentScalarValue<OctoObjectId>(_id);

        Logger.LogInformation(
            "Deleting email domain group rule '{RtId}' from '{ServiceClientServiceUri}'",
            rtId, ServiceClient.ServiceUri);

        await ServiceClient.DeleteEmailDomainGroupRule(rtId);

        Logger.LogInformation("Email domain group rule '{RtId}' deleted", rtId);
    }
}
