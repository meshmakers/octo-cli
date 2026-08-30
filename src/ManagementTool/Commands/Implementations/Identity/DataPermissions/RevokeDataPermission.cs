using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.DataPermissions;

internal class RevokeDataPermission : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _permissionIdArg;
    private readonly IArgument _roleArg;

    public RevokeDataPermission(ILogger<RevokeDataPermission> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "RevokeDataPermission",
            "Revokes a data permission from a role", options, identityServicesClient, authenticationService)
    {
        _permissionIdArg = CommandArgumentValue.AddArgument("pid", "permissionId",
            ["Dot-namespaced permission id"], true, 1);
        _roleArg = CommandArgumentValue.AddArgument("r", "roleName", ["Name of the role"], true, 1);
    }

    public override async Task Execute()
    {
        var permissionId = CommandArgumentValue.GetArgumentScalarValue<string>(_permissionIdArg);
        var roleName = CommandArgumentValue.GetArgumentScalarValue<string>(_roleArg);

        await ServiceClient.RevokeDataPermissionFromRole(permissionId, roleName);
        Logger.LogInformation("Data permission '{PermissionId}' revoked from role '{RoleName}'",
            permissionId, roleName);
    }
}
