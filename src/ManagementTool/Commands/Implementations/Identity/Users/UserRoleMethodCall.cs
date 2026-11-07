using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Users;

/// <summary>
///     AB#8417: <c>AddUserToRole</c> and <c>RemoveUserFromRole</c> run the CK methods <c>System.Identity/User.AddRole-1</c>
///     and <c>User.RemoveRole-1</c> through the <see cref="RtUserMethodClient" /> generated in GraphQlDtos from
///     <c>System.Identity-[3.2,4.0)</c>, instead of the obsolete REST methods <c>IIdentityServicesClient.AddRoleToUser</c> /
///     <c>RemoveRoleFromUser</c>. The methods address the user by runtime id, so the user is looked up by name first
///     (<see cref="IIdentityServicesClient.GetUserByNameEmailOrId" />, <see cref="UserDto.UserId" />).
/// </summary>
internal sealed class UserRoleMethodCall(
    IIdentityServicesClient identityServicesClient,
    ITenantClient tenantClient,
    IAuthenticationService authenticationService)
{
    public Task AddRoleAsync(string userName, string roleName) =>
        RunAsync(userName, roleName, (users, rtId) => users.AddRoleAsync(rtId, roleName));

    public Task RemoveRoleAsync(string userName, string roleName) =>
        RunAsync(userName, roleName, (users, rtId) => users.RemoveRoleAsync(rtId, roleName));

    private async Task RunAsync(string userName, string roleName,
        Func<RtUserMethodClient, string, Task<CkMethodResult>> call)
    {
        var user = await identityServicesClient.GetUserByNameEmailOrId(userName);
        if (string.IsNullOrWhiteSpace(user?.UserId))
        {
            throw new ServiceClientException($"User with name '{userName}' not found.");
        }

        // The tenant client has its own access token; the command's PreValidate only authenticated the identity client.
        await authenticationService.EnsureAuthenticated(tenantClient.AccessToken);

        var result = await call(new RtUserMethodClient(tenantClient), user.UserId);
        if (!result.IsSuccess)
        {
            // A declared error of the method: the same message the REST endpoint answered, prefixed with the code.
            // Logged by the Runner like a refused REST call before (exit code -3).
            throw new ServiceClientException(CkMethodErrorText.Format(result.Error!,
                DescribeDeclaredError(result.Error!.Code, user.Name ?? userName, roleName)));
        }
    }

    internal static string? DescribeDeclaredError(string code, string userName, string roleName) => code switch
    {
        RtUserMethodClient.AddRoleErrors.RoleNotFound => $"Role with name '{roleName}' not found.",
        RtUserMethodClient.AddRoleErrors.RoleAlreadyAssigned => $"User '{userName}' already has role '{roleName}'",
        RtUserMethodClient.RemoveRoleErrors.RoleNotAssigned => $"User '{userName}' doesn't have role '{roleName}'",
        _ => null
    };
}
