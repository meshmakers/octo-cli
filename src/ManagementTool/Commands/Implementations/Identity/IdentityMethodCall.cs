using System.Security.Cryptography;
using System.Text;
using GraphQL;
using GraphQlDtos;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity;

/// <summary>
///     Runs one identity administration command through a CK method client of <c>System.Identity</c> (identity wave 2:
///     groups, clients, roles, API scopes and resources, data permissions and policies, identity providers, e-mail domain
///     group rules, external tenant user mappings, admin provisioning, secrets and client mirrors), generated in GraphQlDtos
///     from <c>System.Identity-[3.9,4.0)</c> (the mirror role list of <c>SetClientMirrorRoles</c> needs 3.9.0, the other
///     methods 3.8.0).
///     <list type="bullet">
///         <item>
///             The tenant client is authenticated first: it carries its own access token, the command's
///             <c>PreValidate</c> authenticated only the identity client.
///         </item>
///         <item>
///             There is <b>no REST fallback</b>: these commands need an identity service with the wave 2 methods
///             (<c>System.Identity</c> <see cref="RequiredSystemIdentityVersion" />). A server without the method - the asset
///             repository rejects the document in GraphQL validation, no handler of the method is live
///             (<c>METHOD_HANDLER_NOT_AVAILABLE</c>), or the identity service sends no runtime id for the target - ends the
///             command with a <see cref="ServiceClientException" /> that names the needed version (exit -3). Nothing was
///             changed in that case.
///         </item>
///         <item>
///             A declared error of the method (e.g. <c>GROUP_ALREADY_EXISTS</c>) ends the command with a
///             <see cref="ServiceClientException" /> <c>CODE: message (parameters)</c>; the Runner logs it and the command
///             exits with -3. Declared errors the REST endpoint answered with success (e.g. <c>MEMBER_ALREADY_IN_GROUP</c>)
///             stay a success (<c>treatAsSuccess</c>). Other refusals (<c>METHOD_FORBIDDEN</c>,
///             <c>METHOD_VALIDATION_FAILED</c>, <c>METHOD_TARGET_NOT_FOUND</c>) reach the Runner as
///             <see cref="CkMethodCallException" /> (code and parameters, exit -3).
///         </item>
///         <item>
///             With a <c>secret</c> (a secret value the call sends), every text this class produces is scrubbed of it, and
///             an undeclared refusal is rethrown as a <see cref="ServiceClientException" /> with the scrubbed text and without
///             the inner exception, so no part of the request reaches the output.
///         </item>
///     </list>
/// </summary>
internal sealed class IdentityMethodCall(
    ITenantClient tenantClient,
    IAuthenticationService authenticationService,
    ILogger logger)
{
    internal const string Redacted = "[REDACTED]";

    /// <summary>The <c>System.Identity</c> version that carries every method these commands call.</summary>
    internal const string RequiredSystemIdentityVersion = "3.8.0";

    /// <summary>The <c>System.Identity</c> version of <c>Client.SetMirrorRoles-1</c> and <c>Client.MirrorRoleNames</c>.</summary>
    internal const string MirrorRolesSystemIdentityVersion = "3.9.0";

    /// <summary>
    ///     The methods that carry a secret (a parameter or the result). The identity service offers their handlers on the
    ///     method bus only with <c>Methods:Identity:EnablePasswordMethods</c>.
    /// </summary>
    private static readonly HashSet<string> SecretCarryingMethodIds =
    [
        RtClientMethodClient.CreateClientMethodId, RtClientMethodClient.UpdateClientMethodId,
        RtClientMethodClient.AddSecretMethodId, RtClientMethodClient.UpdateSecretMethodId,
        RtClientMethodClient.RemoveSecretMethodId, RtClientMethodClient.RotateMirrorSecretMethodId,
        RtApiResourceMethodClient.AddSecretMethodId, RtApiResourceMethodClient.UpdateSecretMethodId,
        RtApiResourceMethodClient.RemoveSecretMethodId,
        RtGoogleIdentityProviderMethodClient.CreateIdentityProviderMethodId,
        RtGoogleIdentityProviderMethodClient.UpdateIdentityProviderMethodId
    ];

    /// <summary>The tenant client the generated method clients are created over.</summary>
    public ITenantClient TenantClient => tenantClient;

    /// <summary>Runs <paramref name="call" />.</summary>
    /// <param name="methodId">The qualified id of the CK method, for the message when the service lacks it.</param>
    /// <param name="call">The call of the generated method client.</param>
    /// <param name="describeDeclaredError">Optional message for a declared error instead of the service's message.</param>
    /// <param name="secret">A secret value the call sends; scrubbed from every text.</param>
    /// <param name="treatAsSuccess">
    ///     Declared errors the REST endpoint answered with success (e.g. <c>MEMBER_ALREADY_IN_GROUP</c>); the command then
    ///     ends as after a successful call, as before.
    /// </param>
    public async Task RunAsync(string methodId, Func<Task<CkMethodResult>> call,
        Func<CkMethodError, string?>? describeDeclaredError = null, string? secret = null,
        Func<CkMethodError, bool>? treatAsSuccess = null)
    {
        await RunAsync<object>(methodId, async () =>
            {
                var result = await call();
                return result.IsSuccess
                    ? CkMethodResult<object>.Success(result.InvocationId, null)
                    : CkMethodResult<object>.Failure(result.Error!);
            },
            describeDeclaredError, secret, treatAsSuccess);
    }

    /// <summary>Runs <paramref name="call" /> and returns the value of the method result.</summary>
    /// <param name="methodId">The qualified id of the CK method, for the message when the service lacks it.</param>
    /// <param name="call">The call of the generated method client.</param>
    /// <param name="describeDeclaredError">Optional message for a declared error instead of the service's message.</param>
    /// <param name="secret">A secret value the call sends; scrubbed from every text.</param>
    /// <param name="treatAsSuccess">Declared errors the REST endpoint answered with success.</param>
    public async Task<TValue?> RunAsync<TValue>(string methodId, Func<Task<CkMethodResult<TValue>>> call,
        Func<CkMethodError, string?>? describeDeclaredError = null, string? secret = null,
        Func<CkMethodError, bool>? treatAsSuccess = null)
    {
        await authenticationService.EnsureAuthenticated(tenantClient.AccessToken);

        CkMethodResult<TValue> result;
        try
        {
            result = await call();
        }
        catch (CkMethodCallException e) when (CkMethodFallback.IsMethodUnavailable(e))
        {
            logger.LogDebug("The CK method {MethodId} is not available: {Reason}", methodId,
                Scrub(CkMethodFallback.Describe(e), secret));
            throw new ServiceClientException(MethodUnavailableMessage(methodId, tenantClient.ServiceUri?.ToString()));
        }
        catch (RuntimeIdUnavailableException)
        {
            logger.LogDebug("The identity service sent no runtime id for the target of the CK method {MethodId}",
                methodId);
            throw new ServiceClientException(MethodUnavailableMessage(methodId, tenantClient.ServiceUri?.ToString()));
        }
        catch (CkMethodCallException e) when (!string.IsNullOrEmpty(secret))
        {
            throw new ServiceClientException(Scrub(CkMethodErrorText.Format(e), secret));
        }

        if (!result.IsSuccess && treatAsSuccess?.Invoke(result.Error!) == true)
        {
            // The REST endpoint answered this declared error with 200 (e.g. a member that is already in the group).
            return default;
        }

        if (!result.IsSuccess)
        {
            throw new ServiceClientException(Scrub(
                CkMethodErrorText.Format(result.Error!, describeDeclaredError?.Invoke(result.Error!)), secret));
        }

        return result.Value;
    }

    /// <summary>The message of a command whose method the service does not offer; names the needed version.</summary>
    internal static string MethodUnavailableMessage(string methodId, string? serviceUri) =>
        $"The service at '{serviceUri}' does not offer the CK method {methodId}. It needs identity services with " +
        (methodId == RtClientMethodClient.SetMirrorRolesMethodId
            ? $"System.Identity {MirrorRolesSystemIdentityVersion} or newer (the mirror role list)"
            : $"System.Identity {RequiredSystemIdentityVersion} or newer (the identity wave 2 methods)") +
        (SecretCarryingMethodIds.Contains(methodId)
            ? " with the password methods switched on (Methods:Identity:EnablePasswordMethods). "
            : ". ") +
        "Nothing was changed. octo-cli does not fall back to the REST endpoint.";

    /// <summary>
    ///     The role names of <paramref name="roleIds" />: the commands take role runtime ids as the REST endpoints did, the
    ///     methods take role names. An unknown id ends the command before any write, with the message the REST endpoint
    ///     answered (<c>Role with id '…' not found.</c>).
    /// </summary>
    public static async Task<List<string>> ResolveRoleNamesAsync(IIdentityServicesClient identityServicesClient,
        IReadOnlyCollection<string> roleIds)
    {
        if (roleIds.Count == 0)
        {
            return [];
        }

        var roles = (await identityServicesClient.GetRoles()).ToList();
        var names = new List<string>(roleIds.Count);
        foreach (var roleId in roleIds)
        {
            var role = roles.FirstOrDefault(r => string.Equals(r.Id, roleId, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(role?.Name))
            {
                throw new ServiceClientException($"Role with id '{roleId}' not found.");
            }

            names.Add(role.Name);
        }

        // As the REST endpoint (a dictionary by role name): a role named twice is set once.
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    ///     The runtime id of the client <paramref name="clientId" /> (the methods address a client by runtime id, the
    ///     commands by client id). Call it inside the method call delegate: an unknown client fails with the identity
    ///     service's answer, as the REST write did; an identity service that sends no runtime id (older versions) ends the
    ///     command with the version message (<see cref="RuntimeIdUnavailableException" />).
    /// </summary>
    public static async Task<string> ResolveClientRtIdAsync(IIdentityServicesClient identityServicesClient,
        string clientId)
    {
        var client = await identityServicesClient.GetClient(clientId);
        return string.IsNullOrWhiteSpace(client?.RtId) ? throw new RuntimeIdUnavailableException() : client.RtId;
    }

    /// <summary>
    ///     The runtime id of the role <paramref name="roleName" /> (the methods address a role by runtime id, the commands by
    ///     name). Call it inside the method call delegate, as <see cref="ResolveClientRtIdAsync" />.
    /// </summary>
    public static async Task<string> ResolveRoleRtIdAsync(IIdentityServicesClient identityServicesClient,
        string roleName)
    {
        var role = await identityServicesClient.GetRoleByName(roleName);
        return string.IsNullOrWhiteSpace(role?.Id) ? throw new RuntimeIdUnavailableException() : role.Id;
    }

    /// <summary>
    ///     The secret DTO the REST endpoint answered for a new secret, built from the clear text the <c>AddSecret</c> method
    ///     returns once, the way the identity service's REST facade builds it: the stored value is the Base64 SHA-256 of the
    ///     clear text (the identity service's storage format), which identifies the secret for later updates and deletes.
    ///     The clear text is never logged; the command prints the DTO once.
    /// </summary>
    public static ApiSecretDto ToNewSecretDto(string? clearText, DateTime? expirationDate, string? description)
    {
        if (string.IsNullOrEmpty(clearText))
        {
            // The REST endpoint's answer when the method returned no secret.
            throw new ServiceClientException("Creating the secret failed.");
        }

        return new ApiSecretDto
        {
            ValueClearText = clearText,
            ValueEncrypted = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(clearText))),
            ExpirationDate = expirationDate,
            Description = description
        };
    }

    /// <summary>
    ///     The mapping DTO the REST endpoints answer, built from the result record of the admin provisioning and mapping
    ///     methods the same way the identity service's REST facade builds it.
    /// </summary>
    public static ExternalTenantUserMappingDto ToMappingDto(RtAdminProvisioningResultRecordDto? record) =>
        new()
        {
            Id = string.IsNullOrEmpty(record?.MappingRtId) ? null : OctoObjectId.Parse(record.MappingRtId),
            SourceTenantId = record?.SourceTenantId ?? string.Empty,
            SourceUserId = record?.SourceUserId ?? string.Empty,
            SourceUserName = record?.SourceUserName ?? string.Empty,
            RoleIds = record?.RoleIds?.ToList() ?? [],
            GroupNames = record?.GroupNames?.ToList() ?? []
        };

    /// <summary>
    ///     The runtime id of the data permission <paramref name="permissionId" />. An unknown permission ends the command
    ///     before any write, with the REST endpoint's message; call it inside the method call delegate.
    /// </summary>
    public static async Task<string> ResolveDataPermissionRtIdAsync(IIdentityServicesClient identityServicesClient,
        string permissionId)
    {
        var permission = (await identityServicesClient.GetDataPermissions())
            .FirstOrDefault(p => string.Equals(p.PermissionId, permissionId, StringComparison.Ordinal));
        if (permission == null)
        {
            throw new ServiceClientException($"Data permission '{permissionId}' not found.");
        }

        return permission.Id?.ToString() ?? throw new RuntimeIdUnavailableException();
    }

    /// <summary>
    ///     The runtime id of the API scope <paramref name="name" />, read with a GraphQL query of the tenant (the REST DTO
    ///     carries none). An unknown scope ends the command before any write.
    /// </summary>
    public Task<string> ResolveApiScopeRtIdAsync(string name) =>
        ResolveRtIdByNameAsync<RtApiScopeDto>(GraphQlConstants.GetApiScopeRtIdByName, name, d => d.RtId.ToString(),
            $"API scope '{name}' not found.");

    /// <summary>
    ///     The runtime id of the API resource <paramref name="name" />, read with a GraphQL query of the tenant (the REST
    ///     DTO carries none). An unknown resource ends the command before any write.
    /// </summary>
    public Task<string> ResolveApiResourceRtIdAsync(string name) =>
        ResolveRtIdByNameAsync<RtApiResourceDto>(GraphQlConstants.GetApiResourceRtIdByName, name,
            d => d.RtId.ToString(), $"API resource '{name}' not found.");

    private async Task<string> ResolveRtIdByNameAsync<TDto>(string query, string name, Func<TDto, string?> rtIdOf,
        string notFound) where TDto : class
    {
        var result = await tenantClient.SendQueryAsync<TDto>(new GraphQLRequest
        {
            Query = query,
            Variables = new { name }
        });
        var item = result?.Items?.FirstOrDefault();
        var rtId = item == null ? null : rtIdOf(item);
        return string.IsNullOrWhiteSpace(rtId) ? throw new ServiceClientException(notFound) : rtId;
    }

    /// <summary>Replaces every occurrence of <paramref name="secret" /> in <paramref name="text" /> with <see cref="Redacted" />.</summary>
    internal static string Scrub(string text, string? secret) =>
        string.IsNullOrEmpty(secret) ? text : text.Replace(secret, Redacted, StringComparison.Ordinal);
}

/// <summary>
///     Thrown inside a method call delegate when the identity service sent no runtime id for the target (identity versions
///     before the runtime id in the DTO); <see cref="IdentityMethodCall" /> then ends the command with the message that
///     names the needed identity version.
/// </summary>
internal sealed class RuntimeIdUnavailableException() : Exception("The identity service sent no runtime id.");
