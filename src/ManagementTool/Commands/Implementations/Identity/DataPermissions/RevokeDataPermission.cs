using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.DataPermissions;

internal class RevokeDataPermission : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IdentityMethodCall _methods;
    private readonly IArgument _permissionIdArg;
    private readonly IArgument _roleArg;

    public RevokeDataPermission(ILogger<RevokeDataPermission> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "RevokeDataPermission",
            "Revokes a data permission from a role", options, identityServicesClient, authenticationService)
    {
        _methods = new IdentityMethodCall(tenantClient, authenticationService, logger);
        _permissionIdArg = CommandArgumentValue.AddArgument("pid", "permissionId",
            ["Dot-namespaced permission id"], true, 1);
        _roleArg = CommandArgumentValue.AddArgument("r", "roleName", ["Name of the role"], true, 1);
    }

    public override async Task Execute()
    {
        var permissionId = CommandArgumentValue.GetArgumentScalarValue<string>(_permissionIdArg);
        var roleName = CommandArgumentValue.GetArgumentScalarValue<string>(_roleArg);

        await _methods.RunAsync(RtDataPermissionMethodClient.RevokeFromRoleMethodId,
            async () => await new RtDataPermissionMethodClient(_methods.TenantClient).RevokeFromRoleAsync(
                await IdentityMethodCall.ResolveDataPermissionRtIdAsync(ServiceClient, permissionId), roleName),
            treatAsSuccess: e => e.Code == RtDataPermissionMethodClient.RevokeFromRoleErrors.PermissionNotGranted);
        Logger.LogInformation("Data permission '{PermissionId}' revoked from role '{RoleName}'",
            permissionId, roleName);
    }
}
