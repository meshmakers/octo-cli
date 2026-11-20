using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

/// <summary>
///     <c>Client.CreateClient-1</c> and <c>Client.UpdateClient-1</c> with the parameters the identity service's REST facade
///     takes from a <see cref="ClientDto" />, so the client commands keep building their <see cref="ClientDto" /> as before.
///     The method sets the protocol defaults of a new client, the default scopes and the hash of the secret.
/// </summary>
internal static class ClientMethods
{
    /// <summary>Creates the client described by <paramref name="client" />.</summary>
    public static Task<CkMethodResult<string?>> CreateAsync(ITenantClient tenantClient, ClientDto client) =>
        new RtClientMethodClient(tenantClient).CreateClientAsync(client.ClientId!, client.IsEnabled, client.ClientName,
            client.ClientUri, client.AllowedGrantTypes?.ToList(), client.RedirectUris?.ToList(),
            client.PostLogoutRedirectUris?.ToList(), client.AllowedCorsOrigins?.ToList(), client.AllowedScopes?.ToList(),
            client.IsOfflineAccessEnabled, client.RequireClientSecret, client.ClientSecret, client.FrontChannelLogoutUri,
            client.FrontChannelLogoutSessionRequired, client.BackChannelLogoutUri, client.BackChannelLogoutSessionRequired,
            client.AutoProvisionInChildTenants);

    /// <summary>
    ///     Updates the client with runtime id <paramref name="rtId" /> to <paramref name="client" />. As the REST endpoint,
    ///     allowed scopes the DTO does not name are reset (an empty list), not kept.
    /// </summary>
    public static Task<CkMethodResult> UpdateAsync(ITenantClient tenantClient, string rtId, ClientDto client) =>
        new RtClientMethodClient(tenantClient).UpdateClientAsync(rtId, client.ClientId, client.IsEnabled,
            client.ClientName, client.ClientUri, client.AllowedGrantTypes?.ToList(), client.RedirectUris?.ToList(),
            client.PostLogoutRedirectUris?.ToList(), client.AllowedCorsOrigins?.ToList(),
            client.AllowedScopes?.ToList() ?? [], client.IsOfflineAccessEnabled, client.RequireClientSecret,
            client.ClientSecret, client.FrontChannelLogoutUri, client.FrontChannelLogoutSessionRequired,
            client.BackChannelLogoutUri, client.BackChannelLogoutSessionRequired, client.AutoProvisionInChildTenants);
}
