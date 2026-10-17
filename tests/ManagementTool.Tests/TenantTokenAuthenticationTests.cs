using FakeItEasy;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#6836: a command addressing tenant X never hands the service clients a token issued for another
///     tenant. A token of another tenant is exchanged, cached per tenant, and never used as a fallback.
/// </summary>
public sealed class TenantTokenAuthenticationTests
{
    private readonly CountingExchange _exchange = new();

    [Fact]
    public async Task TokenOfTheContextTenant_IsUsedAsIs_WithoutExchange()
    {
        var token = TestJwt.For("child");
        var (service, holder, _) = Create("child", token);

        await service.EnsureAuthenticated(holder);

        Assert.Equal(token, holder.AccessToken);
        Assert.Equal(0, _exchange.Calls);
    }

    [Fact]
    public async Task RootTokenForAChildContext_IsExchanged_AndOnlyTheExchangedTokenReachesTheClients()
    {
        var root = TestJwt.For("root", "root-token");
        var (service, holder, entry) = Create("child", root);

        await service.EnsureAuthenticated(holder);

        Assert.Equal(1, _exchange.Calls);
        Assert.Equal(["child"], _exchange.Targets);
        Assert.NotEqual(root, holder.AccessToken);
        Assert.Equal("child", AccessTokenInspector.TryGetTenantId(holder.AccessToken));
        // The context keeps its own token; the exchanged one is only cached beside it.
        Assert.Equal(root, entry.Authentication.AccessToken);
        Assert.Contains("child", entry.Authentication.TenantTokens!.Keys);
    }

    [Fact]
    public async Task SecondCommandInTheSameProcess_DoesNotExchangeAgain()
    {
        var (service, holder, _) = Create("child", TestJwt.For("root"));

        await service.EnsureAuthenticated(holder);
        await service.EnsureAuthenticated(holder);

        Assert.Equal(1, _exchange.Calls);
    }

    [Fact]
    public async Task SecondInvocation_ReusesTheTokenKeptInTheContext()
    {
        var root = TestJwt.For("root");
        var (first, holder1, entry) = Create("child", root);
        await first.EnsureAuthenticated(holder1);

        // A new process: new provider and service, same context entry.
        var second = CreateOn(entry, new CountingExchange(), out var secondExchange, out var holder2);
        await second.EnsureAuthenticated(holder2);

        Assert.Equal(0, secondExchange.Calls);
        Assert.Equal(holder1.AccessToken, holder2.AccessToken);
    }

    [Fact]
    public async Task RefreshedContextToken_IsExchangedAgain()
    {
        var (service, holder, entry) = Create("child", TestJwt.For("root", "old"));
        await service.EnsureAuthenticated(holder);

        entry.Authentication.AccessToken = TestJwt.For("root", "new");
        var next = CreateOn(entry, _exchange, out _, out var holder2);
        await next.EnsureAuthenticated(holder2);

        Assert.Equal(2, _exchange.Calls);
    }

    [Theory]
    [InlineData(TenantTokenDenialCause.NotProvisioned)]
    [InlineData(TenantTokenDenialCause.AccessToTenantDenied)]
    [InlineData(TenantTokenDenialCause.ClientNotAllowed)]
    [InlineData(TenantTokenDenialCause.SubjectTokenRejected)]
    public async Task Denial_StopsTheCommand_NamesTheTenant_AndNeverFallsBackToTheRootToken(
        TenantTokenDenialCause cause)
    {
        var root = TestJwt.For("root", "root-token");
        _exchange.Deny = cause;
        var (service, holder, _) = Create("child", root);

        var ex = await Assert.ThrowsAsync<TenantTokenExchangeDeniedException>(() => service.EnsureAuthenticated(holder));

        Assert.Equal(cause, ex.Cause);
        Assert.Contains("'child'", ex.Message);
        Assert.Null(holder.AccessToken);
        Assert.DoesNotContain(root, ex.Message);
    }

    [Fact]
    public async Task NotProvisioned_MessageNamesTheRequiredStep()
    {
        _exchange.Deny = TenantTokenDenialCause.NotProvisioned;
        var (service, holder, _) = Create("child", TestJwt.For("root"));

        var ex = await Assert.ThrowsAsync<TenantTokenExchangeDeniedException>(() => service.EnsureAuthenticated(holder));

        Assert.Contains("ProvisionCurrentUser -ttid child", ex.Message);
    }

    [Fact]
    public async Task WithoutExchange_ATokenOfAnotherTenant_IsRefused_NotUsed()
    {
        var root = TestJwt.For("root");
        var entry = NewEntry("child", root);
        var service = new AuthenticationService(
            Options.Create(entry.Authentication), A.Fake<IAuthenticatorClient>(), new StubContextManager(entry));
        var holder = new Holder();

        var ex = await Assert.ThrowsAsync<TenantTokenMismatchException>(() => service.EnsureAuthenticated(holder));

        Assert.Equal("child", ex.TenantId);
        Assert.Null(holder.AccessToken);
    }

    [Fact]
    public async Task ClientCredentialsContext_IsGuardedLikeADeviceContext()
    {
        // No refresh token: the client_credentials branch of EnsureAuthenticated.
        var root = TestJwt.For("root");
        var entry = NewEntry("child", root);
        entry.Authentication.RefreshToken = null;
        entry.Authentication.AccessTokenExpiresAt = DateTime.Now.AddHours(1);
        var service = CreateOn(entry, _exchange, out _, out var holder);

        await service.EnsureAuthenticated(holder);

        Assert.Equal("child", AccessTokenInspector.TryGetTenantId(holder.AccessToken));
        Assert.Equal(1, _exchange.Calls);
    }

    [Fact]
    public async Task ContextWithoutTenant_AndOpaqueTokens_BehaveAsBefore()
    {
        var (noTenant, holder1, _) = Create(null, TestJwt.For("root"));
        await noTenant.EnsureAuthenticated(holder1);
        Assert.Equal(0, _exchange.Calls);
        Assert.NotNull(holder1.AccessToken);

        var (opaque, holder2, _) = Create("child", "opaque-reference-token");
        await opaque.EnsureAuthenticated(holder2);
        Assert.Equal("opaque-reference-token", holder2.AccessToken);
        Assert.Equal(0, _exchange.Calls);
    }

    [Fact]
    public void ContextFile_RoundTripsTheCachedTokens()
    {
        var entry = NewEntry("child", TestJwt.For("root"));
        entry.Authentication.TenantTokens = new Dictionary<string, TenantTokenEntry>
        {
            ["child"] = new() { AccessToken = "t", ExpiresAtUtc = DateTime.UtcNow.AddHours(1), SubjectFingerprint = "f" }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(entry);
        var read = System.Text.Json.JsonSerializer.Deserialize<ContextEntry>(json)!;

        Assert.Equal("f", read.Authentication.TenantTokens!["child"].SubjectFingerprint);

        // A context file written by an older octo-cli has no such field.
        var old = System.Text.Json.JsonSerializer.Deserialize<ContextEntry>(
            """{"OctoToolOptions":{},"Authentication":{"AccessToken":"a"}}""")!;
        Assert.Null(old.Authentication.TenantTokens);
    }

    [Fact]
    public void Store_PersistsIntoTheContext_AndDropsExpiredTokens()
    {
        var entry = NewEntry("child", TestJwt.For("root"));
        entry.Authentication.TenantTokens = new Dictionary<string, TenantTokenEntry>
        {
            ["gone"] = new() { AccessToken = "old", ExpiresAtUtc = DateTime.UtcNow.AddHours(-1), SubjectFingerprint = "f" }
        };
        var contexts = new StubContextManager(entry);
        var store = new ContextTenantTokenStore(contexts);

        store.Set("child", new CachedTenantToken("tok", DateTime.UtcNow.AddHours(1), "fp"));

        Assert.Equal(1, contexts.Saves);
        Assert.Equal("tok", store.Get("child")!.AccessToken);
        Assert.Equal("fp", store.Get("CHILD")!.SubjectFingerprint);
        Assert.Null(store.Get("gone"));
    }

    private (AuthenticationService Service, Holder Holder, ContextEntry Entry) Create(string? contextTenant,
        string accessToken)
    {
        var entry = NewEntry(contextTenant, accessToken);
        var service = CreateOn(entry, _exchange, out _, out var holder);
        return (service, holder, entry);
    }

    private static AuthenticationService CreateOn(ContextEntry entry, CountingExchange exchange,
        out CountingExchange used, out Holder holder)
    {
        used = exchange;
        holder = new Holder();
        var contexts = new StubContextManager(entry);
        var provider = new TenantTokenProvider(exchange, new ContextTenantTokenStore(contexts));
        return new AuthenticationService(Options.Create(entry.Authentication), A.Fake<IAuthenticatorClient>(), contexts,
            provider);
    }

    private static ContextEntry NewEntry(string? tenant, string accessToken) => new()
    {
        OctoToolOptions = new OctoToolOptions { TenantId = tenant },
        Authentication = new OctoToolAuthenticationOptions
        {
            AccessToken = accessToken,
            RefreshToken = "refresh",
            AccessTokenExpiresAt = DateTime.Now.AddHours(1)
        }
    };

    private sealed class Holder : IServiceClientAccessToken
    {
        public string? AccessToken { get; set; }

        public event EventHandler? AccessTokenUpdated
        {
            add { }
            remove { }
        }
    }
}
