using Meshmakers.Octo.Communication.Contracts;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Options;
using NLog;

namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

public class AuthenticationService : IAuthenticationService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly IOptions<OctoToolAuthenticationOptions> _authenticationOptions;
    private readonly IAuthenticatorClient _authenticatorClient;
    private readonly IContextManager _contextManager;
    private readonly ITenantTokenProvider? _tenantTokenProvider;

    public AuthenticationService(IOptions<OctoToolAuthenticationOptions> authenticationOptions,
        IAuthenticatorClient authenticatorClient, IContextManager contextManager,
        ITenantTokenProvider? tenantTokenProvider = null)
    {
        _tenantTokenProvider = tenantTokenProvider;
        _authenticationOptions = authenticationOptions;
        _authenticatorClient = authenticatorClient;
        _contextManager = contextManager;
    }


    public async Task EnsureAuthenticated(IServiceClientAccessToken serviceClientAccessToken)
    {
        if (string.IsNullOrEmpty(_authenticationOptions.Value.AccessToken))
        {
            Logger.Info("No credential data available.");
            return;
        }

        // Device/interactive session: a refresh token is present (device flow requests offline_access).
        if (!string.IsNullOrEmpty(_authenticationOptions.Value.RefreshToken))
        {
            // Decide whether to refresh from our own expiry clock, NOT from a live /connect/userinfo
            // probe (AB#4754). The former probe added a network round-trip before every command and,
            // worse, treated ANY identity-service hiccup (5xx, timeout, DNS, TLS) as "token invalid"
            // and forced a refresh of a still-valid token. A token that is not expiring soon is used
            // as-is; only an expiring/expired one is refreshed. Server-side revocation of a token that
            // has not yet expired is surfaced by the target service's own 401, as before.
            if (!IsTokenExpiringSoon())
            {
                await ApplyAsync(serviceClientAccessToken, _authenticationOptions.Value.AccessToken);
                return;
            }

            try
            {
                var refreshed = await _authenticatorClient.RefreshTokenAsync(_authenticationOptions.Value.RefreshToken);
                SaveAuthenticationData(refreshed);
                await ApplyAsync(serviceClientAccessToken, refreshed.AccessToken);
                Logger.Info("Access token has been refreshed via the refresh token.");
                return;
            }
            catch (AuthenticationFailedException ex)
            {
                // The refresh token itself is gone / expired / revoked — the failure mode that hits
                // the least-frequently-used context first when switching contexts. If client_credentials
                // env vars are set, re-acquire non-interactively (headless/CI); otherwise surface an
                // actionable re-login hint instead of the raw OIDC 'invalid_grant' (AB#4754).
                if (TryReadClientCredentialsEnv(out var fallbackClientId, out var fallbackClientSecret))
                {
                    var reacquired = await _authenticatorClient.RequestClientCredentialsTokenAsync(
                        ApiScopes.OctoApiFullAccess,
                        DefaultScopes.None,
                        customScopes: null,
                        clientId: fallbackClientId,
                        clientSecret: fallbackClientSecret);
                    SaveAuthenticationData(reacquired);
                    await ApplyAsync(serviceClientAccessToken, reacquired.AccessToken);
                    Logger.Info("Refresh token was rejected; re-acquired via client_credentials env vars.");
                    return;
                }

                throw SessionExpired(ex);
            }
        }

        // No refresh token: client_credentials session. While the env vars remain set, silently
        // re-acquire the token when it is expired/near-expiry. Device-code and client_credentials
        // sessions are kept separate; a context carrying a refresh token takes the branch above.
        if (IsTokenExpiringSoon() &&
            TryReadClientCredentialsEnv(out var clientId, out var clientSecret))
        {
            var newAuthData = await _authenticatorClient.RequestClientCredentialsTokenAsync(
                ApiScopes.OctoApiFullAccess,
                DefaultScopes.None,
                customScopes: null,
                clientId: clientId,
                clientSecret: clientSecret);
            SaveAuthenticationData(newAuthData);
            await ApplyAsync(serviceClientAccessToken, newAuthData.AccessToken);
            Logger.Info("Access token re-acquired via client_credentials env vars.");
            return;
        }

        // Use the existing access token (even without refresh token)
        await ApplyAsync(serviceClientAccessToken, _authenticationOptions.Value.AccessToken);
    }

    /// <summary>
    ///     Hands the token to the service clients, but only a token issued for the tenant the effective
    ///     context addresses (AB#6836). A token of another tenant (a context for a child tenant that was
    ///     signed in on the root tenant) is exchanged for a token of the context's tenant, cached per tenant
    ///     and silently renewed from the context's own token. The other token is never handed on as a
    ///     fallback: without an exchange the command stops with the reason.
    /// </summary>
    private async Task ApplyAsync(IServiceClientAccessToken serviceClientAccessToken, string? accessToken)
    {
        var tenantId = _contextManager.GetEffectiveContext()?.OctoToolOptions.TenantId;
        if (!string.IsNullOrWhiteSpace(tenantId) && !string.IsNullOrWhiteSpace(accessToken) &&
            AccessTokenInspector.IsIssuedForOtherTenant(accessToken, tenantId))
        {
            if (_tenantTokenProvider == null)
            {
                throw new TenantTokenMismatchException(tenantId);
            }

            Logger.Info("The token of context '{0}' was issued for tenant '{1}'; exchanging it for tenant '{2}'.",
                _contextManager.GetEffectiveContextName(), AccessTokenInspector.TryGetTenantId(accessToken), tenantId);
            accessToken = await _tenantTokenProvider.GetAccessTokenAsync(tenantId, accessToken);
        }

        serviceClientAccessToken.AccessToken = accessToken;
    }

    // Turns a failed refresh-token exchange into an actionable error: name the context to re-login
    // and point at the non-interactive alternative. Runner renders ToolException as a clean message
    // (exit -5) instead of the raw AuthenticationFailedException OIDC error (exit -4).
    private Exception SessionExpired(Exception inner)
    {
        var contextName = _contextManager.GetEffectiveContextName();
        var loginHint = contextName != null
            ? $"octo-cli --{Constants.ContextArgumentTerm} {contextName} -c LogIn"
            : "octo-cli -c LogIn";

        return new ToolException(
            $"The session for context '{contextName ?? "<none>"}' has expired and could not be refreshed " +
            $"(its refresh token is no longer valid). Re-authenticate with: {loginHint}. For non-interactive " +
            $"use, set {Constants.EnvVarClientId} / {Constants.EnvVarClientSecret} and re-run.",
            inner);
    }

    private bool IsTokenExpiringSoon()
    {
        var expiresAt = _authenticationOptions.Value.AccessTokenExpiresAt;
        if (expiresAt == null)
        {
            // Unknown expiry → treat as expired so we re-acquire defensively.
            return true;
        }

        // Compared in local time because the SDK's AuthenticationData.ExpiresAt is set with DateTime.Now (see Sdk.ServiceClient.Authentication.AuthenticatorClient).
        return expiresAt.Value < DateTime.Now.AddSeconds(30);
    }

    private static bool TryReadClientCredentialsEnv(out string clientId, out string clientSecret)
    {
        clientId = Environment.GetEnvironmentVariable(Constants.EnvVarClientId) ?? string.Empty;
        clientSecret = Environment.GetEnvironmentVariable(Constants.EnvVarClientSecret) ?? string.Empty;
        return !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret);
    }

    public void SaveAuthenticationData(AuthenticationData authenticationData)
    {
        _authenticationOptions.Value.AccessToken = authenticationData.AccessToken;
        _authenticationOptions.Value.RefreshToken = authenticationData.RefreshToken;
        _authenticationOptions.Value.AccessTokenExpiresAt = authenticationData.ExpiresAt;

        // The effective context, not the active one: with --context in play the token belongs to
        // the context the command actually talked to.
        var context = _contextManager.GetEffectiveContext();
        if (context != null)
        {
            context.Authentication.AccessToken = authenticationData.AccessToken;
            context.Authentication.RefreshToken = authenticationData.RefreshToken;
            context.Authentication.AccessTokenExpiresAt = authenticationData.ExpiresAt;
            _contextManager.SaveEffectiveContext();

            Logger.Info("Credential data stored in context '{0}'.", _contextManager.GetEffectiveContextName());
        }
    }
}
