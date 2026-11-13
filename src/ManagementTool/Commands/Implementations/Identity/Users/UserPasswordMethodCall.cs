using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Users;

/// <summary>
///     AB#8418: <c>CreateUser</c> and <c>ResetPassword</c> run the CK methods <c>System.Identity/User.CreateUser-1</c> and
///     <c>User.ResetPassword-1</c> through the <see cref="RtUserMethodClient" /> generated in GraphQlDtos from
///     <c>System.Identity-[3.2,4.0)</c>, instead of the REST methods <c>IIdentityServicesClient.CreateUser</c> /
///     <c>ResetPassword</c>. The password travels only in the GraphQL request body.
///     <para>
///         Unlike the role methods (AB#8838) there is <b>no REST fallback</b> (decision AB#7039): a server without the
///         method - the asset repository rejects the document, or no handler is live because the identity service is too
///         old or runs with <c>Methods:Identity:EnablePasswordMethods=false</c> - ends the command with a message that names
///         the needed identity version. Every refusal is decided by the server; octo-cli keeps no copy of the password
///         policy.
///     </para>
///     <para>
///         The password never reaches output: the texts this class produces are scrubbed of it (<see cref="Scrub" />), and
///         an undeclared refusal is rethrown as a <see cref="ServiceClientException" /> with the scrubbed text and without
///         the inner exception, so the Runner prints the same line (exit code -3) without any part of the request.
///     </para>
/// </summary>
internal sealed class UserPasswordMethodCall(
    IIdentityServicesClient identityServicesClient,
    ITenantClient tenantClient,
    IAuthenticationService authenticationService,
    ILogger logger)
{
    /// <summary>The first identity release that hosts <c>User.CreateUser-1</c> (handler <c>UserCreateUserHandler</c>).</summary>
    internal const string CreateUserMinimumIdentityVersion = "3.5.57";

    /// <summary>The first identity release that hosts <c>User.ResetPassword-1</c> (handler <c>UserResetPasswordHandler</c>).</summary>
    internal const string ResetPasswordMinimumIdentityVersion = "3.5.52";

    internal const string Redacted = "[REDACTED]";

    /// <summary>Creates the user; <paramref name="password" /> is optional (without it the user gets no password).</summary>
    public async Task CreateUserAsync(string userName, string eMail, string? password)
    {
        // The tenant client has its own access token; the command's PreValidate only authenticated the identity client.
        await authenticationService.EnsureAuthenticated(tenantClient.AccessToken);

        var result = await CallAsync(RtUserMethodClient.CreateUserMethodId, CreateUserMinimumIdentityVersion,
            "The user was not created.", password,
            () => new RtUserMethodClient(tenantClient).CreateUserAsync(userName, eMail, password: password));

        ThrowOnDeclaredError(result, password);
    }

    /// <summary>Resets the password of the user <paramref name="userName" /> (looked up by name for its runtime id).</summary>
    public async Task ResetPasswordAsync(string userName, string password)
    {
        var user = await identityServicesClient.GetUserByNameEmailOrId(userName);
        if (string.IsNullOrWhiteSpace(user?.UserId))
        {
            throw new ServiceClientException($"User with name '{userName}' not found.");
        }

        await authenticationService.EnsureAuthenticated(tenantClient.AccessToken);

        var result = await CallAsync(RtUserMethodClient.ResetPasswordMethodId, ResetPasswordMinimumIdentityVersion,
            "The password was not changed.", password,
            () => new RtUserMethodClient(tenantClient).ResetPasswordAsync(user.UserId, password));

        ThrowOnDeclaredError(result, password);
    }

    private async Task<TResult> CallAsync<TResult>(string methodId, string minimumIdentityVersion, string outcome,
        string? password, Func<Task<TResult>> call)
    {
        try
        {
            return await call();
        }
        catch (CkMethodCallException e) when (CkMethodFallback.IsMethodUnavailable(e))
        {
            logger.LogDebug("The CK method {MethodId} is not available: {Reason}", methodId,
                Scrub(CkMethodFallback.Describe(e), password));
            throw new ServiceClientException(MethodUnavailableMessage(methodId, minimumIdentityVersion, outcome,
                tenantClient.ServiceUri?.ToString()));
        }
        catch (CkMethodCallException e)
        {
            // An undeclared refusal (METHOD_FORBIDDEN, METHOD_TARGET_NOT_FOUND, …): the line the Runner printed for a
            // CkMethodCallException, scrubbed, without the inner exception.
            throw new ServiceClientException(Scrub(CkMethodErrorText.Format(e), password));
        }
    }

    private static void ThrowOnDeclaredError(CkMethodResult result, string? password)
    {
        if (!result.IsSuccess)
        {
            // A declared error of the method (USER_ALREADY_EXISTS, PASSWORD_POLICY_VIOLATION, …): code, the server's
            // message (e.g. the policy description) and parameters. Logged by the Runner with exit code -3.
            throw new ServiceClientException(Scrub(CkMethodErrorText.Format(result.Error!), password));
        }
    }

    /// <summary>Replaces every occurrence of <paramref name="secret" /> in <paramref name="text" /> with <see cref="Redacted" />.</summary>
    internal static string Scrub(string text, string? secret) =>
        string.IsNullOrEmpty(secret) ? text : text.Replace(secret, Redacted, StringComparison.Ordinal);

    internal static string MethodUnavailableMessage(string methodId, string minimumIdentityVersion, string outcome,
        string? serviceUri) =>
        $"The service at '{serviceUri}' does not offer the CK method {methodId}. It needs identity services " +
        $"{minimumIdentityVersion} or newer with the password methods switched on (Methods:Identity:EnablePasswordMethods). " +
        $"{outcome} octo-cli sends passwords only through the CK method and does not fall back to the REST endpoint.";
}
