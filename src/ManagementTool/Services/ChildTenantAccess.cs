using Meshmakers.Octo.Communication.Contracts;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.ServiceClient.AiServices;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Meshmakers.Octo.Sdk.ServiceClient.ReportingServices;
using Meshmakers.Octo.Sdk.ServiceClient.TenantLifecycle;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

/// <summary>
///     The token the caller holds <b>for a child tenant</b> (AB#7192): the capability endpoints of a tenant belong to the
///     tenant's own administrator, and a token of the parent tenant is refused on a child's routes.
/// </summary>
public interface IChildTenantAccess
{
    /// <summary>
    ///     Returns an access token issued for <paramref name="childTenantId" />. For a client-credentials session (no
    ///     refresh token) with <c>OCTO_CLI_CLIENT_ID</c> and a secret set it requests a client-credentials token for the
    ///     child - the client's mirror there, e.g. the creator of a scratch tenant. The secret is
    ///     <c>OCTO_CLI_CHILD_CLIENT_SECRET</c> (the mirror's own secret) when set, else <c>OCTO_CLI_CLIENT_SECRET</c>. For a user session the
    ///     token of the context is exchanged for the child (RFC 8693). The context's own token is never used as a fallback.
    /// </summary>
    /// <exception cref="ToolException">No token can be obtained; the message names the cause.</exception>
    Task<string> GetAccessTokenAsync(string childTenantId);
}

/// <inheritdoc />
public sealed class ChildTenantAccess(
    IOptions<OctoToolAuthenticationOptions> authenticationOptions,
    IAuthenticatorClient authenticatorClient,
    ITenantTokenProvider? tenantTokenProvider = null,
    Func<string, string?>? environment = null) : IChildTenantAccess
{
    private readonly Func<string, string?> _environment = environment ?? Environment.GetEnvironmentVariable;

    /// <inheritdoc />
    public async Task<string> GetAccessTokenAsync(string childTenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childTenantId);

        var authentication = authenticationOptions.Value;
        var isServiceSession = string.IsNullOrEmpty(authentication.RefreshToken);
        var clientId = _environment(Constants.EnvVarClientId);
        // The mirror's OWN secret when given; the inherited parent secret only without it (it stops working in the child
        // once the inherited copy is removed, AB#5061).
        var childSecret = _environment(Constants.EnvVarChildClientSecret);
        var clientSecret = string.IsNullOrEmpty(childSecret) ? _environment(Constants.EnvVarClientSecret) : childSecret;

        if (isServiceSession && !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
        {
            try
            {
                var data = await authenticatorClient.RequestClientCredentialsTokenForTenantAsync(childTenantId,
                    ApiScopes.OctoApiFullAccess, DefaultScopes.None, clientId, clientSecret);
                return data.AccessToken ?? throw new ToolException(
                    $"The identity service issued no access token for tenant '{childTenantId}'.");
            }
            catch (AuthenticationFailedException ex)
            {
                throw new ToolException(
                    $"The client '{clientId}' could not get a token for tenant '{childTenantId}': {ex.Message} " +
                    "A client has a mirror there only when it is flagged AutoProvisionInChildTenants, and it is " +
                    "administrator of a child it did not create only when it is the creator of a scratch tenant " +
                    "(Create --scratch). Check with GetTenants.", ex);
            }
        }

        if (!isServiceSession && !string.IsNullOrEmpty(authentication.AccessToken) && tenantTokenProvider != null)
        {
            try
            {
                return await tenantTokenProvider.GetAccessTokenAsync(childTenantId, authentication.AccessToken);
            }
            catch (TenantTokenExchangeDeniedException ex)
            {
                throw new ToolException(
                    $"No token for tenant '{childTenantId}' could be obtained from the signed-in user: {ex.Message} " +
                    $"Provision yourself there (ProvisionCurrentUser -ttid {childTenantId}) or use a context of that tenant.",
                    ex);
            }
        }

        throw new ToolException(
            $"No credentials to act in tenant '{childTenantId}'. Set {Constants.EnvVarClientId} and " +
            $"{Constants.EnvVarClientSecret} (the client that created the scratch tenant; {Constants.EnvVarChildClientSecret} " +
            "for the secret of its mirror in the child, if it differs) and run LogInClientCredentials for the parent " +
            "context, or sign in as a user who is administrator of that tenant.");
    }
}

/// <summary>The capabilities of one child tenant, read and switched with that tenant's own token (AB#7192).</summary>
public interface IChildTenantFeatures
{
    /// <summary>Reads which capabilities the child has enabled.</summary>
    Task<TenantFeaturesStatusDto> GetStatusAsync();

    /// <summary>The disable calls of the four capabilities.</summary>
    ITenantFeatureSwitches Switches { get; }
}

/// <summary>Builds the clients for the capabilities of a child tenant from the service URLs of the context.</summary>
public interface IChildTenantFeaturesFactory
{
    /// <summary>The capabilities of <paramref name="childTenantId" />, addressed with <paramref name="accessToken" />.</summary>
    IChildTenantFeatures Create(string childTenantId, string accessToken);
}

/// <inheritdoc />
public sealed class ChildTenantFeaturesFactory(IOptions<OctoToolOptions> options) : IChildTenantFeaturesFactory
{
    /// <inheritdoc />
    public IChildTenantFeatures Create(string childTenantId, string accessToken)
    {
        var urls = options.Value;
        var token = new ServiceClientAccessToken { AccessToken = accessToken };

        var assetClient = new AssetServicesClient(
            new AssetServiceClientOptions { EndpointUri = urls.AssetServiceUrl, TenantId = childTenantId }, token);
        var streamData = new StreamDataServicesClient(
            new StreamDataServiceClientOptions { EndpointUri = urls.AssetServiceUrl, TenantId = childTenantId }, token);
        var communication = new CommunicationServicesClient(
            new CommunicationServiceClientOptions { EndpointUri = urls.CommunicationServiceUrl, TenantId = childTenantId },
            token);
        var reporting = new ReportingServicesClient(
            new ReportingServicesClientOptions { EndpointUri = urls.ReportingServiceUrl, TenantId = childTenantId },
            token);
        var ai = new AiServicesClient(
            new AiServiceClientOptions { EndpointUri = urls.AiServiceUrl, TenantId = childTenantId }, token);

        return new ChildTenantFeatures(assetClient.GetTenantFeaturesStatusAsync,
            new DelegateTenantFeatureSwitches(
                () => ai.DisableAsync(childTenantId),
                () => communication.DisableAsync(childTenantId),
                () => reporting.DisableAsync(childTenantId),
                () => streamData.DisableAsync(childTenantId)));
    }

    private sealed class ChildTenantFeatures(Func<Task<TenantFeaturesStatusDto>> status, ITenantFeatureSwitches switches)
        : IChildTenantFeatures
    {
        public Task<TenantFeaturesStatusDto> GetStatusAsync() => status();

        public ITenantFeatureSwitches Switches { get; } = switches;
    }
}
