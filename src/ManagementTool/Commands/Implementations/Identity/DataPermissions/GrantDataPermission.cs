using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.DataPermissions;

internal class GrantDataPermission : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IdentityMethodCall _methods;
    private readonly IArgument _permissionIdArg;
    private readonly IArgument _roleArg;

    public GrantDataPermission(ILogger<GrantDataPermission> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "GrantDataPermission",
            "Grants a data permission to a role", options, identityServicesClient, authenticationService)
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

        await _methods.RunAsync(RtDataPermissionMethodClient.GrantToRoleMethodId,
            async () => await new RtDataPermissionMethodClient(_methods.TenantClient).GrantToRoleAsync(
                await IdentityMethodCall.ResolveDataPermissionRtIdAsync(ServiceClient, permissionId), roleName),
            treatAsSuccess: e => e.Code == RtDataPermissionMethodClient.GrantToRoleErrors.PermissionAlreadyGranted);
        Logger.LogInformation("Data permission '{PermissionId}' granted to role '{RoleName}'",
            permissionId, roleName);
    }
}
