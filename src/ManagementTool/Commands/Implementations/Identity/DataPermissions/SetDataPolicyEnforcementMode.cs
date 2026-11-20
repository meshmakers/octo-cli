using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.DataPermissions;

internal class SetDataPolicyEnforcementMode : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IdentityMethodCall _methods;
    private readonly IArgument _policyRtIdArg;
    private readonly IArgument _modeArg;

    public SetDataPolicyEnforcementMode(ILogger<SetDataPolicyEnforcementMode> logger,
        IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "SetDataPolicyEnforcementMode",
            "Switches a data policy between Enforce and AuditOnly (the operator flip, AB#4974)", options,
            identityServicesClient, authenticationService)
    {
        _methods = new IdentityMethodCall(tenantClient, authenticationService, logger);
        _policyRtIdArg = CommandArgumentValue.AddArgument("id", "policyRtId", ["RtId of the policy"], true, 1);
        _modeArg = CommandArgumentValue.AddArgument("m", "enforcementMode", ["Enforce or AuditOnly"], true, 1);
    }

    public override async Task Execute()
    {
        var policyRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_policyRtIdArg);
        var mode = CommandArgumentValue.GetArgumentScalarValue<string>(_modeArg);

        if (!Enum.TryParse<RtDataPolicyEnforcementModeEnum>(mode, true, out var enforcementMode))
        {
            // The REST endpoint's answer to an unknown mode, before any write.
            throw new ServiceClientException("EnforcementMode must be Enforce|AuditOnly.");
        }

        await _methods.RunAsync(RtDataPolicyMethodClient.SetEnforcementModeMethodId,
            () => new RtDataPolicyMethodClient(_methods.TenantClient).SetEnforcementModeAsync(policyRtId,
                enforcementMode));
        Logger.LogInformation("Data policy '{PolicyRtId}' switched to '{Mode}'", policyRtId, mode);
        Logger.LogInformation("Note: the change takes effect within 60 seconds (asset repository policy cache)");
    }
}
