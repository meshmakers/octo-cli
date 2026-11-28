using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Blueprints;

/// <summary>
///     AB#8452: the asset repository client of one child tenant of the context's tenant, carrying the caller's token for
///     that child, so <c>InstallBlueprint --child-tenant-id</c> installs into a child from a context of its parent.
/// </summary>
public interface IChildTenantAssetClientFactory
{
    /// <summary>
    ///     Checks that <paramref name="childTenantId" /> is a child tenant (at any depth) of the context's tenant, gets the
    ///     caller's token for it through <see cref="IChildTenantAccess" /> and returns the child's id (lower case) and
    ///     its client.
    /// </summary>
    /// <exception cref="ToolException">
    ///     The tenant is the context's own tenant, is no child of it, or no token for it can be obtained.
    /// </exception>
    Task<(string TenantId, IAssetServicesClient Client)> CreateAsync(string childTenantId);
}

/// <inheritdoc />
/// <remarks>
///     The token path of <c>Delete --disable-features</c> and of the client role commands with
///     <c>--child-tenant-id</c>: a user session exchanges the context's token for the child, a client-credentials session
///     requests a token for the client's mirror in the child. The context's own token is never sent to the child's routes.
/// </remarks>
public sealed class ChildTenantAssetClientFactory(
    IOptions<OctoToolOptions> options,
    IAssetServicesClient assetServicesClient,
    IAuthenticationService authenticationService,
    IChildTenantAccess childTenantAccess) : IChildTenantAssetClientFactory
{
    /// <inheritdoc />
    public async Task<(string TenantId, IAssetServicesClient Client)> CreateAsync(string childTenantId)
    {
        var urls = options.Value;
        var childId = await ChildTenantCheck.EnsureDescendantAsync(urls, assetServicesClient, authenticationService,
            childTenantId);
        var accessToken = await childTenantAccess.GetAccessTokenAsync(childId);

        var client = new AssetServicesClient(
            new AssetServiceClientOptions { EndpointUri = urls.AssetServiceUrl, TenantId = childId },
            new ServiceClientAccessToken { AccessToken = accessToken });
        return (childId, client);
    }
}
