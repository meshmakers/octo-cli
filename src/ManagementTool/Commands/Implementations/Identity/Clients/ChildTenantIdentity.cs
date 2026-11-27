using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

/// <summary>
///     AB#9872: the identity and tenant (GraphQL) clients of one child tenant, both carrying the caller's token for that
///     child, so the client role commands act in a child tenant from a context of its parent.
/// </summary>
/// <param name="TenantId">The child tenant the clients address (lower case).</param>
/// <param name="Identity">The identity service client of the child (<c>{child}/v1/...</c>).</param>
/// <param name="Tenant">The GraphQL client of the child on the asset repository (CK method calls).</param>
/// <param name="Authentication">
///     Hands the child token to a client; never the context's token (the context addresses the parent).
/// </param>
public sealed record ChildTenantIdentity(
    string TenantId,
    IIdentityServicesClient Identity,
    ITenantClient Tenant,
    IAuthenticationService Authentication);

/// <summary>Builds the <see cref="ChildTenantIdentity" /> of a child tenant of the context's tenant (AB#9872).</summary>
public interface IChildTenantIdentityFactory
{
    /// <summary>
    ///     Checks that <paramref name="childTenantId" /> is a child tenant (at any depth) of the context's tenant, gets the
    ///     caller's token for it through <see cref="IChildTenantAccess" /> and returns the clients of the child.
    /// </summary>
    /// <exception cref="ToolException">
    ///     The tenant is the context's own tenant, is no child of it (unknown or below another parent), or no token for it
    ///     can be obtained; the message names the tenant and the cause.
    /// </exception>
    Task<ChildTenantIdentity> CreateAsync(string childTenantId);
}

/// <inheritdoc />
/// <remarks>
///     The token comes from <see cref="IChildTenantAccess" />, the path of <c>Delete --disable-features</c> and of the
///     setup of a scratch tenant: a user session exchanges the context's token for the child (RFC 8693, the identity
///     service allows it only for a descendant where the user is provisioned with a role), a client-credentials session
///     requests a token for the client's mirror in the child. The context's own token is never sent to the child's routes.
/// </remarks>
public sealed class ChildTenantIdentityFactory(
    IOptions<OctoToolOptions> options,
    IAssetServicesClient assetServicesClient,
    IAuthenticationService authenticationService,
    IChildTenantAccess childTenantAccess) : IChildTenantIdentityFactory
{
    /// <summary>
    ///     The clients of <paramref name="childTenantId" /> from <paramref name="factory" />, which the commands take as an
    ///     optional dependency (their other constructors stay as they were).
    /// </summary>
    internal static Task<ChildTenantIdentity> CreateForAsync(IChildTenantIdentityFactory? factory,
        string childTenantId) =>
        (factory ?? throw new InvalidOperationException("No factory for --child-tenant-id is registered."))
        .CreateAsync(childTenantId);

    /// <inheritdoc />
    public async Task<ChildTenantIdentity> CreateAsync(string childTenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childTenantId);

        var childId = childTenantId.Trim().ToLowerInvariant();
        var urls = options.Value;
        var contextTenantId = urls.TenantId?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(contextTenantId))
        {
            throw new ToolException(
                "The context names no tenant: --child-tenant-id needs a context of the parent tenant.");
        }

        if (childId == contextTenantId)
        {
            throw new ToolException(
                $"Tenant '{childId}' is the tenant of the context, not a child of it: omit --child-tenant-id.");
        }

        await authenticationService.EnsureAuthenticated(assetServicesClient.AccessToken);
        var descendants = await assetServicesClient.GetTenantDescendantsAsync();
        if (!descendants.Any(t => string.Equals(t.TenantId, childId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ToolException(
                $"Tenant '{childId}' is not a child tenant of '{contextTenantId}' at " +
                $"'{assetServicesClient.ServiceUri.GetLeftPart(UriPartial.Authority)}': it does not exist or belongs to " +
                "another parent. GetTenants -r lists the child tenants of the context.");
        }

        var accessToken = await childTenantAccess.GetAccessTokenAsync(childId);

        var identity = new IdentityServicesClient(
            new IdentityServiceClientOptions { EndpointUri = urls.IdentityServiceUrl, TenantId = childId },
            new ServiceClientAccessToken { AccessToken = accessToken });
        var tenant = new TenantClient(
            new TenantClientOptions { EndpointUri = urls.AssetServiceUrl, TenantId = childId },
            new ServiceClientAccessToken { AccessToken = accessToken });

        return new ChildTenantIdentity(childId, identity, tenant, new FixedTokenAuthentication(accessToken));
    }

    /// <summary>
    ///     Hands one fixed token (the child's) to a client. <see cref="IdentityMethodCall" /> authenticates its tenant
    ///     client before every call; with the context's <see cref="AuthenticationService" /> that would put the parent's
    ///     token (or one exchanged for the context's tenant) on the child's routes.
    /// </summary>
    public sealed class FixedTokenAuthentication(string accessToken) : IAuthenticationService
    {
        /// <inheritdoc />
        public Task EnsureAuthenticated(IServiceClientAccessToken serviceClientAccessToken)
        {
            serviceClientAccessToken.AccessToken = accessToken;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void SaveAuthenticationData(AuthenticationData authenticationData) =>
            throw new InvalidOperationException(
                "The token of a child tenant is not stored in the context of its parent.");
    }
}
