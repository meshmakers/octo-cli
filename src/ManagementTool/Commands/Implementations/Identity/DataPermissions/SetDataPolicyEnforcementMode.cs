using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.DataPermissions;

internal class SetDataPolicyEnforcementMode : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _policyRtIdArg;
    private readonly IArgument _modeArg;

    public SetDataPolicyEnforcementMode(ILogger<SetDataPolicyEnforcementMode> logger,
        IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "SetDataPolicyEnforcementMode",
            "Switches a data policy between Enforce and AuditOnly (the operator flip, AB#4974)", options,
            identityServicesClient, authenticationService)
    {
        _policyRtIdArg = CommandArgumentValue.AddArgument("id", "policyRtId", ["RtId of the policy"], true, 1);
        _modeArg = CommandArgumentValue.AddArgument("m", "enforcementMode", ["Enforce or AuditOnly"], true, 1);
    }

    public override async Task Execute()
    {
        var policyRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_policyRtIdArg);
        var mode = CommandArgumentValue.GetArgumentScalarValue<string>(_modeArg);

        await ServiceClient.SetDataPolicyEnforcementMode(policyRtId, mode);
        Logger.LogInformation("Data policy '{PolicyRtId}' switched to '{Mode}'", policyRtId, mode);
    }
}
