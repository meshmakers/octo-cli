using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.DataPermissions;

internal class CreateDataPolicy : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _permissionIdArg;
    private readonly IArgument _targetsArg;
    private readonly IArgument _actionsArg;
    private readonly IArgument _scopeArg;
    private readonly IArgument _modeArg;

    public CreateDataPolicy(ILogger<CreateDataPolicy> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "CreateDataPolicy",
            "Creates a policy bound to a data permission (AB#4972)", options, identityServicesClient,
            authenticationService)
    {
        _permissionIdArg = CommandArgumentValue.AddArgument("pid", "permissionId",
            ["Dot-namespaced permission id"], true, 1);
        _targetsArg = CommandArgumentValue.AddArgument("t", "targetCkTypeIds",
            ["Comma-separated CK type ids the policy protects (derived types inherit)"], true, 1);
        _actionsArg = CommandArgumentValue.AddArgument("a", "actions",
            ["Comma-separated actions: Read, Write, Delete"], true, 1);
        _scopeArg = CommandArgumentValue.AddArgument("s", "scope",
            ["All or OwnedOnly (default All)"], false, 1);
        _modeArg = CommandArgumentValue.AddArgument("m", "enforcementMode",
            ["Enforce or AuditOnly (default Enforce)"], false, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(Samples:
        [
            new CodeSample(arguments:
            [
                new CodeSampleArgument(_permissionIdArg, "accounting.documents"),
                new CodeSampleArgument(_targetsArg, "Meshmakers.Accounting/UploadedDocument"),
                new CodeSampleArgument(_actionsArg, "Read,Write")
            ], description: "Owned-only read/write policy")
        ]);

    public override async Task Execute()
    {
        var permissionId = CommandArgumentValue.GetArgumentScalarValue<string>(_permissionIdArg);
        var dto = new DataPolicyDto
        {
            TargetCkTypeIds = SplitList(CommandArgumentValue.GetArgumentScalarValue<string>(_targetsArg)),
            Actions = SplitList(CommandArgumentValue.GetArgumentScalarValue<string>(_actionsArg)),
            Scope = CommandArgumentValue.IsArgumentUsed(_scopeArg)
                ? CommandArgumentValue.GetArgumentScalarValue<string>(_scopeArg)
                : "All",
            EnforcementMode = CommandArgumentValue.IsArgumentUsed(_modeArg)
                ? CommandArgumentValue.GetArgumentScalarValue<string>(_modeArg)
                : "Enforce"
        };

        var policyRtId = await ServiceClient.CreateDataPolicy(permissionId, dto);
        Logger.LogInformation("Data policy '{PolicyRtId}' created for permission '{PermissionId}'",
            policyRtId, permissionId);
    }

    private static List<string> SplitList(string value)
    {
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}
