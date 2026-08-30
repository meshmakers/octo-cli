using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.DataPermissions;

internal class GrantDataPermission : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _permissionIdArg;
    private readonly IArgument _roleArg;

    public GrantDataPermission(ILogger<GrantDataPermission> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "GrantDataPermission",
            "Grants a data permission to a role", options, identityServicesClient, authenticationService)
    {
        _permissionIdArg = CommandArgumentValue.AddArgument("pid", "permissionId",
            ["Dot-namespaced permission id"], true, 1);
        _roleArg = CommandArgumentValue.AddArgument("r", "roleName", ["Name of the role"], true, 1);
    }

    public override async Task Execute()
    {
        var permissionId = CommandArgumentValue.GetArgumentScalarValue<string>(_permissionIdArg);
        var roleName = CommandArgumentValue.GetArgumentScalarValue<string>(_roleArg);

        await ServiceClient.GrantDataPermissionToRole(permissionId, roleName);
        Logger.LogInformation("Data permission '{PermissionId}' granted to role '{RoleName}'",
            permissionId, roleName);
    }
}
