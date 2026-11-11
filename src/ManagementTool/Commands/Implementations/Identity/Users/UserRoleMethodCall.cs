using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Users;

/// <summary>
///     AB#8417: <c>AddUserToRole</c> and <c>RemoveUserFromRole</c> run the CK methods <c>System.Identity/User.AddRole-1</c>
///     and <c>User.RemoveRole-1</c> through the <see cref="RtUserMethodClient" /> generated in GraphQlDtos from
///     <c>System.Identity-[3.2,4.0)</c>, instead of the obsolete REST methods <c>IIdentityServicesClient.AddRoleToUser</c> /
///     <c>RemoveRoleFromUser</c>. The methods address the user by runtime id, so the user is looked up by name first
///     (<see cref="IIdentityServicesClient.GetUserByNameEmailOrId" />, <see cref="UserDto.UserId" />).
///     <para>
///         AB#8838: servers of the released 3.5.x line before the role methods (asset repository before 3.5.43, identity
///         before 3.5.52) reject the method document in GraphQL validation (<c>VARIABLES_ARE_INPUT_TYPES</c>,
///         <c>KNOWN_TYPE_NAMES</c>, <c>FIELDS_ON_CORRECT_TYPE</c>). Then - and when no handler of the method is live - the
///         call was not executed, and the role is changed through the REST endpoint those servers serve
///         (<see cref="CkMethodFallback" />, the same rule as <c>Dump</c>).
///     </para>
/// </summary>
internal sealed class UserRoleMethodCall(
    IIdentityServicesClient identityServicesClient,
    ITenantClient tenantClient,
    IAuthenticationService authenticationService,
    ILogger logger)
{
    public Task AddRoleAsync(string userName, string roleName) =>
        RunAsync(userName, roleName, RtUserMethodClient.AddRoleMethodId,
            (users, rtId) => users.AddRoleAsync(rtId, roleName),
#pragma warning disable CS0618 // AB#8838: the REST method is the fallback for servers without the CK method.
            () => identityServicesClient.AddRoleToUser(userName, roleName));
#pragma warning restore CS0618

    public Task RemoveRoleAsync(string userName, string roleName) =>
        RunAsync(userName, roleName, RtUserMethodClient.RemoveRoleMethodId,
            (users, rtId) => users.RemoveRoleAsync(rtId, roleName),
#pragma warning disable CS0618 // AB#8838: the REST method is the fallback for servers without the CK method.
            () => identityServicesClient.RemoveRoleFromUser(userName, roleName));
#pragma warning restore CS0618

    private async Task RunAsync(string userName, string roleName, string methodId,
        Func<RtUserMethodClient, string, Task<CkMethodResult>> call, Func<Task> restFallback)
    {
        var user = await identityServicesClient.GetUserByNameEmailOrId(userName);
        if (string.IsNullOrWhiteSpace(user?.UserId))
        {
            throw new ServiceClientException($"User with name '{userName}' not found.");
        }

        // The tenant client has its own access token; the command's PreValidate only authenticated the identity client.
        await authenticationService.EnsureAuthenticated(tenantClient.AccessToken);

        CkMethodResult result;
        try
        {
            result = await call(new RtUserMethodClient(tenantClient), user.UserId);
        }
        catch (CkMethodCallException e) when (CkMethodFallback.IsMethodUnavailable(e))
        {
            logger.LogInformation(
                "The CK method {MethodId} is not available ({Reason}); the role is changed through the REST endpoint of earlier identity versions",
                methodId, CkMethodFallback.Describe(e));
            await restFallback();
            return;
        }

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
