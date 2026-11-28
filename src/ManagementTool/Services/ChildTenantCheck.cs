using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;

namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

/// <summary>
///     The check every <c>--child-tenant-id</c> option runs before it asks for a token of the child (AB#9872, AB#8452): the
///     tenant is a child (at any depth) of the context's tenant, never the context's own tenant.
/// </summary>
internal static class ChildTenantCheck
{
    /// <summary>
    ///     Returns the normalized id of <paramref name="childTenantId" /> when it is a descendant of the context's tenant.
    /// </summary>
    /// <exception cref="ToolException">
    ///     The context names no tenant, the tenant is the context's own tenant, or it is no child of it (unknown or below
    ///     another parent); the message names the tenant and the cause.
    /// </exception>
    internal static async Task<string> EnsureDescendantAsync(OctoToolOptions options,
        IAssetServicesClient assetServicesClient, IAuthenticationService authenticationService, string childTenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childTenantId);

        var childId = childTenantId.Trim().ToLowerInvariant();
        var contextTenantId = options.TenantId?.Trim().ToLowerInvariant();
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

        return childId;
    }
}
