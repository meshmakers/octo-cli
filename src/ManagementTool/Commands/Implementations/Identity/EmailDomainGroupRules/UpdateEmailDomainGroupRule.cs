using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.EmailDomainGroupRules;

internal class UpdateEmailDomainGroupRule : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _description;
    private readonly IArgument _emailDomainPattern;
    private readonly IArgument _id;
    private readonly IArgument _targetGroupRtId;

    public UpdateEmailDomainGroupRule(ILogger<UpdateEmailDomainGroupRule> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "UpdateEmailDomainGroupRule",
            "Updates an email domain group rule.", options, identityServicesClient, authenticationService)
    {
        _id = CommandArgumentValue.AddArgument("id", "identifier",
            ["ID of the email domain group rule"], true, 1);
        _emailDomainPattern = CommandArgumentValue.AddArgument("edp", "emailDomainPattern",
            ["Email domain pattern to match (e.g. meshmakers.com)"], true, 1);
        _targetGroupRtId = CommandArgumentValue.AddArgument("tgid", "targetGroupRtId",
            ["RtId of the target group"], true, 1);
        _description = CommandArgumentValue.AddArgument("d", "description",
            ["Optional description of the rule"], false, 1);
    }

    public override async Task Execute()
    {
        var rtId = CommandArgumentValue.GetArgumentScalarValue<OctoObjectId>(_id);

        Logger.LogInformation(
            "Updating email domain group rule '{RtId}' at '{ServiceClientServiceUri}'",
            rtId, ServiceClient.ServiceUri);

        var dto = new EmailDomainGroupRuleDto
        {
            EmailDomainPattern = CommandArgumentValue.GetArgumentScalarValue<string>(_emailDomainPattern),
            TargetGroupRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_targetGroupRtId),
            Description = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_description)
        };
        await ServiceClient.UpdateEmailDomainGroupRule(rtId, dto);

        Logger.LogInformation("Email domain group rule '{RtId}' updated", rtId);
    }
}
