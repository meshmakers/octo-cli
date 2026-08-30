using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.DataPermissions;

internal class GetDataPermissions : ServiceClientOctoCommand<IIdentityServicesClient>
{
    public GetDataPermissions(ILogger<GetDataPermissions> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "GetDataPermissions",
            "Gets all data permissions with their policies and role grants", options, identityServicesClient,
            authenticationService)
    {
    }

    public override async Task Execute()
    {
        var permissions = (await ServiceClient.GetDataPermissions()).ToList();
        Logger.LogInformation("{Count} data permission(s)", permissions.Count);
        foreach (var permission in permissions)
        {
            Logger.LogInformation("Permission '{PermissionId}' ({Id}) — roles: {Roles}",
                permission.PermissionId, permission.Id, string.Join(", ", permission.GrantedRoleNames));
            foreach (var policy in permission.Policies)
            {
                Logger.LogInformation(
                    "  Policy {Id}: targets [{Targets}], actions [{Actions}], scope {Scope}, mode {Mode}",
                    policy.Id, string.Join(", ", policy.TargetCkTypeIds), string.Join(", ", policy.Actions),
                    policy.Scope, policy.EnforcementMode);
            }
        }
    }
}
