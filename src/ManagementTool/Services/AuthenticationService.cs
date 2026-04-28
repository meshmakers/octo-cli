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

        // If we have a refresh token, try to refresh the access token if needed
        if (!string.IsNullOrEmpty(_authenticationOptions.Value.RefreshToken))
        {
            var ensureAuthenticationData = await
                _authenticatorClient.EnsureAuthenticatedAsync(_authenticationOptions.Value.RefreshToken,
                    _authenticationOptions.Value.AccessToken);

            if (ensureAuthenticationData.IsRefreshDone)
            {
                SaveAuthenticationData(ensureAuthenticationData.RefreshedAuthenticationData);
                serviceClientAccessToken.AccessToken = ensureAuthenticationData.RefreshedAuthenticationData.AccessToken;
                Logger.Info("Credential data has been refreshed.");
                return;
            }
        }
        // No refresh token, but client_credentials env vars are set →
        // silently re-acquire token if expired/near-expiry. Device flow always
        // provides a refresh token (offline_access), so this branch only fires
        // for client_credentials sessions.
        // Note: when a device-code session's refresh token IS present (the `if` branch above),
        // we never fall back into this client_credentials branch, even if env vars are set —
        // device-code and client_credentials sessions are kept separate by design.
        else if (IsTokenExpiringSoon() &&
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

        var activeContext = _contextManager.GetActiveContext();
        if (activeContext != null)
        {
            activeContext.Authentication.AccessToken = authenticationData.AccessToken;
            activeContext.Authentication.RefreshToken = authenticationData.RefreshToken;
            activeContext.Authentication.AccessTokenExpiresAt = authenticationData.ExpiresAt;
            _contextManager.SaveActiveContext();
        }
    }
}
