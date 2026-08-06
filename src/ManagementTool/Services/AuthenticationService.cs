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

    public AuthenticationService(IOptions<OctoToolAuthenticationOptions> authenticationOptions,
        IAuthenticatorClient authenticatorClient, IContextManager contextManager)
    {
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
                serviceClientAccessToken.AccessToken = _authenticationOptions.Value.AccessToken;
                return;
            }

            try
            {
                var refreshed = await _authenticatorClient.RefreshTokenAsync(_authenticationOptions.Value.RefreshToken);
                SaveAuthenticationData(refreshed);
                serviceClientAccessToken.AccessToken = refreshed.AccessToken;
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
                    serviceClientAccessToken.AccessToken = reacquired.AccessToken;
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
            serviceClientAccessToken.AccessToken = newAuthData.AccessToken;
            Logger.Info("Access token re-acquired via client_credentials env vars.");
            return;
        }

        // Use the existing access token (even without refresh token)
        serviceClientAccessToken.AccessToken = _authenticationOptions.Value.AccessToken;
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
