using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.EmailDomainGroupRules;

internal class CreateEmailDomainGroupRule : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _description;
    private readonly IArgument _emailDomainPattern;
    private readonly IArgument _targetGroupRtId;

    public CreateEmailDomainGroupRule(ILogger<CreateEmailDomainGroupRule> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "CreateEmailDomainGroupRule",
            "Creates an email domain group rule.", options, identityServicesClient, authenticationService)
    {
        _emailDomainPattern = CommandArgumentValue.AddArgument("edp", "emailDomainPattern",
            ["Email domain pattern to match (e.g. meshmakers.com)"], true, 1);
        _targetGroupRtId = CommandArgumentValue.AddArgument("tgid", "targetGroupRtId",
            ["RtId of the target group"], true, 1);
        _description = CommandArgumentValue.AddArgument("d", "description",
            ["Optional description of the rule"], false, 1);
    }

    public override async Task Execute()
    {
        var emailDomainPattern = CommandArgumentValue.GetArgumentScalarValue<string>(_emailDomainPattern);

        Logger.LogInformation(
            "Creating email domain group rule '{EmailDomainPattern}' at '{ServiceClientServiceUri}'",
            emailDomainPattern, ServiceClient.ServiceUri);

        var dto = new EmailDomainGroupRuleDto
        {
            EmailDomainPattern = emailDomainPattern,
            TargetGroupRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_targetGroupRtId),
            Description = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_description)
        };
        await ServiceClient.CreateEmailDomainGroupRule(dto);

        Logger.LogInformation("Email domain group rule '{EmailDomainPattern}' created", emailDomainPattern);
    }
}
