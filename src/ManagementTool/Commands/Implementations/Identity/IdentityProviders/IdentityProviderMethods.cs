using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.IdentityProviders;

/// <summary>
///     The methods of <c>System.Identity/IdentityProvider</c> are declared on the abstract base and run on the provider's
///     concrete type, so every concrete provider type has its own generated method client. This class picks the client of
///     the type of a provider DTO (the polymorphic REST DTO the commands work with).
/// </summary>
internal static class IdentityProviderMethods
{
    /// <summary>
    ///     The parameters of <c>CreateIdentityProvider-1</c> / <c>UpdateIdentityProvider-1</c>, taken from the DTO the way the
    ///     identity service's REST facade takes them (the fields of the provider type, the others null).
    /// </summary>
    private sealed record Parameters(string Name, bool? Enabled, string? Description, bool? AllowSelfRegistration,
        string? DefaultGroupRtId, string? ClientId = null, string? ClientSecret = null, string? TenantId = null,
        string? Authority = null, string? Host = null, int? Port = null, bool? UseTls = null, string? UserBaseDn = null,
        string? UserNameAttribute = null, string? ParentTenantId = null);

    /// <summary>The write-only client secret of <paramref name="provider" />, when its type has one.</summary>
    public static string? ClientSecretOf(IdentityProviderDto provider) => ParametersOf(provider).ClientSecret;

    /// <summary>
    ///     <c>IdentityProvider.CreateIdentityProvider-1</c> on the concrete type of <paramref name="provider" />; returns
    ///     the runtime id of the new provider.
    /// </summary>
    public static Task<CkMethodResult<string?>> CreateAsync(ITenantClient tenantClient, IdentityProviderDto provider)
    {
        var p = ParametersOf(provider);
        return provider switch
        {
            GoogleIdentityProviderDto => new RtGoogleIdentityProviderMethodClient(tenantClient).CreateIdentityProviderAsync(p.Name, p.Enabled, p.Description, p.AllowSelfRegistration, p.DefaultGroupRtId, p.ClientId, p.ClientSecret, p.TenantId, p.Authority, p.Host, p.Port, p.UseTls, p.UserBaseDn, p.UserNameAttribute, p.ParentTenantId),
            MicrosoftIdentityProviderDto => new RtMicrosoftIdentityProviderMethodClient(tenantClient).CreateIdentityProviderAsync(p.Name, p.Enabled, p.Description, p.AllowSelfRegistration, p.DefaultGroupRtId, p.ClientId, p.ClientSecret, p.TenantId, p.Authority, p.Host, p.Port, p.UseTls, p.UserBaseDn, p.UserNameAttribute, p.ParentTenantId),
            FacebookIdentityProviderDto => new RtFacebookIdentityProviderMethodClient(tenantClient).CreateIdentityProviderAsync(p.Name, p.Enabled, p.Description, p.AllowSelfRegistration, p.DefaultGroupRtId, p.ClientId, p.ClientSecret, p.TenantId, p.Authority, p.Host, p.Port, p.UseTls, p.UserBaseDn, p.UserNameAttribute, p.ParentTenantId),
            AzureEntraIdProviderDto => new RtAzureEntraIdIdentityProviderMethodClient(tenantClient).CreateIdentityProviderAsync(p.Name, p.Enabled, p.Description, p.AllowSelfRegistration, p.DefaultGroupRtId, p.ClientId, p.ClientSecret, p.TenantId, p.Authority, p.Host, p.Port, p.UseTls, p.UserBaseDn, p.UserNameAttribute, p.ParentTenantId),
            MicrosoftAdProviderDto => new RtMicrosoftAdIdentityProviderMethodClient(tenantClient).CreateIdentityProviderAsync(p.Name, p.Enabled, p.Description, p.AllowSelfRegistration, p.DefaultGroupRtId, p.ClientId, p.ClientSecret, p.TenantId, p.Authority, p.Host, p.Port, p.UseTls, p.UserBaseDn, p.UserNameAttribute, p.ParentTenantId),
            OpenLdapProviderDto => new RtOpenLdapIdentityProviderMethodClient(tenantClient).CreateIdentityProviderAsync(p.Name, p.Enabled, p.Description, p.AllowSelfRegistration, p.DefaultGroupRtId, p.ClientId, p.ClientSecret, p.TenantId, p.Authority, p.Host, p.Port, p.UseTls, p.UserBaseDn, p.UserNameAttribute, p.ParentTenantId),
            OctoTenantIdentityProviderDto => new RtOctoTenantIdentityProviderMethodClient(tenantClient).CreateIdentityProviderAsync(p.Name, p.Enabled, p.Description, p.AllowSelfRegistration, p.DefaultGroupRtId, p.ClientId, p.ClientSecret, p.TenantId, p.Authority, p.Host, p.Port, p.UseTls, p.UserBaseDn, p.UserNameAttribute, p.ParentTenantId),
            _ => throw new ServiceClientException("Unknown identity provider type.")
        };
    }

    /// <summary>
    ///     <c>IdentityProvider.UpdateIdentityProvider-1</c> on the concrete type of <paramref name="provider" />. A null or
    ///     empty client secret keeps the stored one.
    /// </summary>
    public static Task<CkMethodResult> UpdateAsync(ITenantClient tenantClient, string rtId,
        IdentityProviderDto provider)
    {
        var p = ParametersOf(provider);
        return provider switch
        {
            GoogleIdentityProviderDto => new RtGoogleIdentityProviderMethodClient(tenantClient).UpdateIdentityProviderAsync(rtId, p.Name, p.Enabled, p.Description, p.AllowSelfRegistration, p.DefaultGroupRtId, p.ClientId, p.ClientSecret, p.TenantId, p.Authority, p.Host, p.Port, p.UseTls, p.UserBaseDn, p.UserNameAttribute, p.ParentTenantId),
            MicrosoftIdentityProviderDto => new RtMicrosoftIdentityProviderMethodClient(tenantClient).UpdateIdentityProviderAsync(rtId, p.Name, p.Enabled, p.Description, p.AllowSelfRegistration, p.DefaultGroupRtId, p.ClientId, p.ClientSecret, p.TenantId, p.Authority, p.Host, p.Port, p.UseTls, p.UserBaseDn, p.UserNameAttribute, p.ParentTenantId),
            FacebookIdentityProviderDto => new RtFacebookIdentityProviderMethodClient(tenantClient).UpdateIdentityProviderAsync(rtId, p.Name, p.Enabled, p.Description, p.AllowSelfRegistration, p.DefaultGroupRtId, p.ClientId, p.ClientSecret, p.TenantId, p.Authority, p.Host, p.Port, p.UseTls, p.UserBaseDn, p.UserNameAttribute, p.ParentTenantId),
            AzureEntraIdProviderDto => new RtAzureEntraIdIdentityProviderMethodClient(tenantClient).UpdateIdentityProviderAsync(rtId, p.Name, p.Enabled, p.Description, p.AllowSelfRegistration, p.DefaultGroupRtId, p.ClientId, p.ClientSecret, p.TenantId, p.Authority, p.Host, p.Port, p.UseTls, p.UserBaseDn, p.UserNameAttribute, p.ParentTenantId),
            MicrosoftAdProviderDto => new RtMicrosoftAdIdentityProviderMethodClient(tenantClient).UpdateIdentityProviderAsync(rtId, p.Name, p.Enabled, p.Description, p.AllowSelfRegistration, p.DefaultGroupRtId, p.ClientId, p.ClientSecret, p.TenantId, p.Authority, p.Host, p.Port, p.UseTls, p.UserBaseDn, p.UserNameAttribute, p.ParentTenantId),
            OpenLdapProviderDto => new RtOpenLdapIdentityProviderMethodClient(tenantClient).UpdateIdentityProviderAsync(rtId, p.Name, p.Enabled, p.Description, p.AllowSelfRegistration, p.DefaultGroupRtId, p.ClientId, p.ClientSecret, p.TenantId, p.Authority, p.Host, p.Port, p.UseTls, p.UserBaseDn, p.UserNameAttribute, p.ParentTenantId),
            OctoTenantIdentityProviderDto => new RtOctoTenantIdentityProviderMethodClient(tenantClient).UpdateIdentityProviderAsync(rtId, p.Name, p.Enabled, p.Description, p.AllowSelfRegistration, p.DefaultGroupRtId, p.ClientId, p.ClientSecret, p.TenantId, p.Authority, p.Host, p.Port, p.UseTls, p.UserBaseDn, p.UserNameAttribute, p.ParentTenantId),
            _ => throw new ServiceClientException("Unknown identity provider type.")
        };
    }

    private static Parameters ParametersOf(IdentityProviderDto dto)
    {
        var common = new Parameters(dto.Name ?? string.Empty, dto.IsEnabled, dto.Description, dto.AllowSelfRegistration,
            dto.DefaultGroupRtId);
        return dto switch
        {
            GoogleIdentityProviderDto g => common with { ClientId = g.ClientId, ClientSecret = g.ClientSecret },
            MicrosoftIdentityProviderDto m => common with { ClientId = m.ClientId, ClientSecret = m.ClientSecret },
            FacebookIdentityProviderDto f => common with { ClientId = f.ClientId, ClientSecret = f.ClientSecret },
            AzureEntraIdProviderDto a => common with
            {
                ClientId = a.ClientId, ClientSecret = a.ClientSecret, TenantId = a.TenantId, Authority = a.Authority
            },
            MicrosoftAdProviderDto ad => common with { Host = ad.Host, Port = ad.Port, UseTls = ad.UseTls },
            OpenLdapProviderDto ldap => common with
            {
                Host = ldap.Host, Port = ldap.Port, UseTls = ldap.UseTls, UserBaseDn = ldap.UserBaseDn,
                UserNameAttribute = ldap.UserNameAttribute
            },
            OctoTenantIdentityProviderDto octo => common with { ParentTenantId = octo.ParentTenantId },
            _ => common
        };
    }

    /// <summary>
    ///     <c>IdentityProvider.DeleteIdentityProvider-1</c> on the concrete type of <paramref name="provider" />. A provider
    ///     type without a client throws <see cref="RuntimeIdUnavailableException" /> (the version message).
    /// </summary>
    public static Task<CkMethodResult> DeleteAsync(ITenantClient tenantClient, IdentityProviderDto provider,
        string rtId) => provider switch
    {
        GoogleIdentityProviderDto => new RtGoogleIdentityProviderMethodClient(tenantClient).DeleteIdentityProviderAsync(rtId),
        MicrosoftIdentityProviderDto => new RtMicrosoftIdentityProviderMethodClient(tenantClient).DeleteIdentityProviderAsync(rtId),
        FacebookIdentityProviderDto => new RtFacebookIdentityProviderMethodClient(tenantClient).DeleteIdentityProviderAsync(rtId),
        AzureEntraIdProviderDto => new RtAzureEntraIdIdentityProviderMethodClient(tenantClient).DeleteIdentityProviderAsync(rtId),
        MicrosoftAdProviderDto => new RtMicrosoftAdIdentityProviderMethodClient(tenantClient).DeleteIdentityProviderAsync(rtId),
        OpenLdapProviderDto => new RtOpenLdapIdentityProviderMethodClient(tenantClient).DeleteIdentityProviderAsync(rtId),
        OctoTenantIdentityProviderDto => new RtOctoTenantIdentityProviderMethodClient(tenantClient).DeleteIdentityProviderAsync(rtId),
        _ => throw new RuntimeIdUnavailableException()
    };
}
