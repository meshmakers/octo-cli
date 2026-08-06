using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meshmakers.Octo.Communication.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Options;
using Xunit;

namespace Meshmakers.Octo.Frontend.ManagementTool.Tests;

/// <summary>
/// Coverage for <see cref="AuthenticationService.EnsureAuthenticated" /> after AB#4754: the device
/// refresh is decided from the stored expiry timestamp (not a live /userinfo probe), and a dead
/// refresh token either falls back to client_credentials env vars or fails with an actionable error.
/// </summary>
public class AuthenticationServiceTests
{
    private const string EnvClientId = "OCTO_CLI_CLIENT_ID";
    private const string EnvClientSecret = "OCTO_CLI_CLIENT_SECRET";

    private const string StoredAccessToken = "stored-access-token";
    private const string StoredRefreshToken = "stored-refresh-token";

    [Fact]
    public async Task ValidToken_NotExpiring_UsesStoredTokenWithoutRefresh()
    {
        var auth = new FakeAuthenticatorClient();
        var service = CreateService(auth,
            accessToken: StoredAccessToken,
            refreshToken: StoredRefreshToken,
            expiresAt: DateTime.Now.AddMinutes(30));

        var token = new AccessTokenHolder();
        await service.EnsureAuthenticated(token);

        Assert.Equal(StoredAccessToken, token.AccessToken);
        Assert.Equal(0, auth.RefreshCalls);
        Assert.Equal(0, auth.ClientCredentialsCalls);
    }

    [Fact]
    public async Task ExpiringToken_RefreshesAndStoresNewToken()
    {
        var auth = new FakeAuthenticatorClient
        {
            RefreshResult = new AuthenticationData
            {
                AccessToken = "fresh-access-token",
                RefreshToken = "fresh-refresh-token",
                ExpiresAt = DateTime.Now.AddMinutes(30)
            }
        };
        var service = CreateService(auth,
            accessToken: StoredAccessToken,
            refreshToken: StoredRefreshToken,
            expiresAt: DateTime.Now.AddSeconds(5));

        var token = new AccessTokenHolder();
        await service.EnsureAuthenticated(token);

        Assert.Equal("fresh-access-token", token.AccessToken);
        Assert.Equal(1, auth.RefreshCalls);
        Assert.Equal(0, auth.ClientCredentialsCalls);
    }

    [Fact]
    public async Task DeadRefreshToken_NoEnv_ThrowsActionableToolException()
    {
        using var _ = new EnvScope((EnvClientId, null), (EnvClientSecret, null));

        var auth = new FakeAuthenticatorClient { RefreshThrows = true };
        var service = CreateService(auth,
            accessToken: StoredAccessToken,
            refreshToken: StoredRefreshToken,
            expiresAt: DateTime.Now.AddSeconds(-5),
            effectiveContextName: "prod-1_meshmakers");

        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            service.EnsureAuthenticated(new AccessTokenHolder()));

        Assert.Contains("prod-1_meshmakers", ex.Message);
        Assert.Contains("LogIn", ex.Message);
        Assert.Equal(1, auth.RefreshCalls);
        Assert.Equal(0, auth.ClientCredentialsCalls);
    }

    [Fact]
    public async Task DeadRefreshToken_WithEnv_ReacquiresViaClientCredentials()
    {
        using var _ = new EnvScope((EnvClientId, "ci-client"), (EnvClientSecret, "ci-secret"));

        var auth = new FakeAuthenticatorClient
        {
            RefreshThrows = true,
            ClientCredentialsResult = new AuthenticationData
            {
                AccessToken = "cc-access-token",
                ExpiresAt = DateTime.Now.AddMinutes(30)
            }
        };
        var service = CreateService(auth,
            accessToken: StoredAccessToken,
            refreshToken: StoredRefreshToken,
            expiresAt: DateTime.Now.AddSeconds(-5));

        var token = new AccessTokenHolder();
        await service.EnsureAuthenticated(token);

        Assert.Equal("cc-access-token", token.AccessToken);
        Assert.Equal(1, auth.RefreshCalls);
        Assert.Equal(1, auth.ClientCredentialsCalls);
        Assert.Equal("ci-client", auth.LastClientId);
    }

    [Fact]
    public async Task ClientCredentialsSession_Expired_WithEnv_Reacquires()
    {
        using var _ = new EnvScope((EnvClientId, "ci-client"), (EnvClientSecret, "ci-secret"));

        var auth = new FakeAuthenticatorClient
        {
            ClientCredentialsResult = new AuthenticationData
            {
                AccessToken = "cc-access-token",
                ExpiresAt = DateTime.Now.AddMinutes(30)
            }
        };
        // No refresh token → client_credentials session.
        var service = CreateService(auth,
            accessToken: StoredAccessToken,
            refreshToken: null,
            expiresAt: DateTime.Now.AddSeconds(-5));

        var token = new AccessTokenHolder();
        await service.EnsureAuthenticated(token);

        Assert.Equal("cc-access-token", token.AccessToken);
        Assert.Equal(0, auth.RefreshCalls);
        Assert.Equal(1, auth.ClientCredentialsCalls);
    }

    private static AuthenticationService CreateService(
        IAuthenticatorClient authenticatorClient,
        string? accessToken,
        string? refreshToken,
        DateTime? expiresAt,
        string? effectiveContextName = "test-context")
    {
        var options = Options.Create(new OctoToolAuthenticationOptions
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiresAt = expiresAt
        });

        return new AuthenticationService(options, authenticatorClient,
            new FakeContextManager(effectiveContextName));
    }

    private sealed class AccessTokenHolder : IServiceClientAccessToken
    {
        public string? AccessToken { get; set; }

        // Not raised by the code under test; empty accessors keep it off the field-like-event path
        // (and out of the unused-event warning that this project treats as an error).
        public event EventHandler? AccessTokenUpdated
        {
            add { }
            remove { }
        }
    }

    private sealed class FakeAuthenticatorClient : IAuthenticatorClient
    {
        public int RefreshCalls { get; private set; }
        public int ClientCredentialsCalls { get; private set; }
        public string? LastClientId { get; private set; }

        public bool RefreshThrows { get; init; }
        public AuthenticationData RefreshResult { get; init; } = new();
        public AuthenticationData ClientCredentialsResult { get; init; } = new();

        public Task<AuthenticationData> RefreshTokenAsync(string refreshToken)
        {
            RefreshCalls++;
            if (RefreshThrows)
            {
                throw new AuthenticationFailedException("Authentication request failed with message: invalid_grant",
                    null);
            }

            return Task.FromResult(RefreshResult);
        }

        public Task<AuthenticationData> RequestClientCredentialsTokenAsync(ApiScopes apiScopes,
            DefaultScopes defaultScopes, IEnumerable<string>? customScopes = null,
            string? clientId = null, string? clientSecret = null)
        {
            ClientCredentialsCalls++;
            LastClientId = clientId;
            return Task.FromResult(ClientCredentialsResult);
        }

        public Task<EnsureAuthenticatedData> EnsureAuthenticatedAsync(string refreshToken, string accessToken) =>
            throw new NotImplementedException();

        public Task<DeviceAuthenticationRequestData> RequestDeviceAuthorizationAsync(ApiScopes apiScopes,
            IEnumerable<string>? customScopes = null) => throw new NotImplementedException();

        public Task<DeviceAuthenticationData> RequestDeviceTokenAsync(string deviceCode) =>
            throw new NotImplementedException();

        public Task<AuthenticationData> RequestPasswordTokenAsync(string username, string password,
            ApiScopes apiScopes, IEnumerable<string>? customScopes = null) =>
            throw new NotImplementedException();

        public Task<bool> IntrospectApiResource(string accessToken, string apiName, string apiSecret) =>
            throw new NotImplementedException();

        public Task<Meshmakers.Octo.Sdk.ServiceClient.Authorization.UserInfoData> GetUserInfoAsync(
            string accessToken) => throw new NotImplementedException();
    }

    // Returns no effective ContextEntry, so SaveAuthenticationData only updates the in-memory options
    // (the persistence path is exercised by ContextManagerTests). The name feeds the re-login hint.
    private sealed class FakeContextManager : IContextManager
    {
        private readonly string? _effectiveContextName;

        public FakeContextManager(string? effectiveContextName)
        {
            _effectiveContextName = effectiveContextName;
        }

        public string? GetEffectiveContextName() => _effectiveContextName;
        public ContextEntry? GetEffectiveContext() => null;
        public void SaveEffectiveContext() { }

        public string ConfigurationFilePath => "<test>";
        public bool IsContextOverridden => false;
        public ContextConfiguration Load() => new();
        public ContextEntry? GetActiveContext() => null;
        public string? GetActiveContextName() => _effectiveContextName;
        public void SelectContext(string name) => throw new NotImplementedException();
        public void AddOrUpdateContext(string name, ContextEntry entry) => throw new NotImplementedException();
        public void RemoveContext(string name) => throw new NotImplementedException();
        public void SetActiveContext(string name) => throw new NotImplementedException();
        public IReadOnlyDictionary<string, ContextEntry> ListContexts() => throw new NotImplementedException();
        public void MigrateIfNeeded() => throw new NotImplementedException();
    }

    // Sets environment variables for the duration of a test and restores their previous values on
    // dispose, so the client_credentials env-var branches can be exercised deterministically.
    private sealed class EnvScope : IDisposable
    {
        private readonly (string Name, string? Previous)[] _restore;

        public EnvScope(params (string Name, string? Value)[] variables)
        {
            _restore = new (string, string?)[variables.Length];
            for (var i = 0; i < variables.Length; i++)
            {
                _restore[i] = (variables[i].Name, Environment.GetEnvironmentVariable(variables[i].Name));
                Environment.SetEnvironmentVariable(variables[i].Name, variables[i].Value);
            }
        }

        public void Dispose()
        {
            foreach (var (name, previous) in _restore)
            {
                Environment.SetEnvironmentVariable(name, previous);
            }
        }
    }
}
