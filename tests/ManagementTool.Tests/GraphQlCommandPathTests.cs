using FakeItEasy;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.GraphQl;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#7027, the real command path: command, authentication service and asset services client are production
///     classes; only identity (a counting fake) and the asset repository (a loopback listener) are replaced. The
///     request is observed on the wire, so the context token reaching the GraphQL route of another tenant cannot
///     go unnoticed. The context token carries a canary that must appear nowhere.
/// </summary>
public sealed class GraphQlCommandPathTests : IClassFixture<WireService>
{
    private const string Canary = "CANARY-CONTEXT-TOKEN-7027";
    private readonly CountingExchange _exchange = new();
    private readonly RecordingLogger<GraphQlCommand> _logger = new();
    private readonly WireService _service;
    private readonly FakeStreams _streams = new();

    // One listener per class: registering a prefix costs ~5 s per instance on macOS.
    public GraphQlCommandPathTests(WireService service)
    {
        _service = service;
        lock (_service.Requests)
        {
            _service.Requests.Clear();
            _service.Authorization.Clear();
            _service.Bodies.Clear();
        }

        _service.Status = 200;
        _service.Body = """{"data":{"ok":true}}""";
    }

    [Fact]
    public async Task ChildTenantFromARootContext_SendsOnlyTheTenantToken_ToTheGraphQlRoute()
    {
        var root = TestJwt.For("root", Canary);

        await Run(root, "-tid", "child", "-q", "{ ok }");

        Assert.Equal(["POST /tenants/child/GraphQL"], _service.Requests);
        var sent = Assert.Single(_service.Authorization);
        Assert.Equal("child", AccessTokenInspector.TryGetTenantId(sent["Bearer ".Length..]));
        Assert.DoesNotContain(Canary, TokenPayload(sent));
        Assert.Equal(1, _exchange.Calls);
        Assert.Equal(["child"], _exchange.Targets);
        Assert.Equal(["""{"data":{"ok":true}}"""], _streams.Output);
        AssertCanaryNowhere(root);
    }

    [Fact]
    public async Task OwnTenant_UsesTheContextToken_WithoutExchange()
    {
        var token = TestJwt.For("root", "own");

        await Run(token, "-q", "{ ok }");

        Assert.Equal(["POST /tenants/root/GraphQL"], _service.Requests);
        Assert.Equal($"Bearer {token}", _service.Authorization.Single());
        Assert.Equal(0, _exchange.Calls);
    }

    [Fact]
    public async Task Body_ReachesTheServiceAsGraphQlJson()
    {
        await Run(TestJwt.For("root"), "-tid", "child", "-q", "query Q($n:Int){ok}", "-vars", """{"n":1}""", "-on", "Q");

        using var body = System.Text.Json.JsonDocument.Parse(_service.Bodies.Single());
        Assert.Equal("query Q($n:Int){ok}", body.RootElement.GetProperty("query").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("variables").GetProperty("n").GetInt32());
        Assert.Equal("Q", body.RootElement.GetProperty("operationName").GetString());
    }

    [Theory]
    [InlineData(TenantTokenDenialCause.NotProvisioned)]
    [InlineData(TenantTokenDenialCause.AccessToTenantDenied)]
    [InlineData(TenantTokenDenialCause.ClientNotAllowed)]
    [InlineData(TenantTokenDenialCause.SubjectTokenRejected)]
    public async Task Denial_StopsBeforeAnyRequest_NamesTheTenant_AndNeverFallsBackToTheContextToken(
        TenantTokenDenialCause cause)
    {
        _exchange.Deny = cause;
        var root = TestJwt.For("root", Canary);

        var ex = await Assert.ThrowsAsync<TenantTokenExchangeDeniedException>(() =>
            Run(root, "-tid", "child", "-q", "{ ok }"));

        Assert.Equal(cause, ex.Cause);
        Assert.Contains("'child'", ex.Message);
        Assert.Empty(_service.Requests);
        Assert.Empty(_service.Authorization);
        Assert.Empty(_streams.Output);
        AssertCanaryNowhere(root, ex.ToString());
    }

    [Fact]
    public async Task NotProvisioned_MessageNamesTheProvisioningStep()
    {
        _exchange.Deny = TenantTokenDenialCause.NotProvisioned;

        var ex = await Assert.ThrowsAsync<TenantTokenExchangeDeniedException>(() =>
            Run(TestJwt.For("root"), "-tid", "child", "-q", "{ ok }"));

        Assert.Contains("ProvisionCurrentUser -ttid child", ex.Message);
    }

    [Fact]
    public async Task GraphQlErrorsFromTheService_ArePassedOnStandardError_WithoutTheToken()
    {
        _service.Body = """{"errors":[{"message":"Unknown field"}]}""";
        var root = TestJwt.For("root", Canary);

        await Assert.ThrowsAsync<ToolException>(() => Run(root, "-tid", "child", "-q", "{ nope }"));

        Assert.Equal(["""{"errors":[{"message":"Unknown field"}]}"""], _streams.Error);
        Assert.Empty(_streams.Output);
        AssertCanaryNowhere(root);
    }

    [Fact]
    public async Task Forbidden_FromTheService_NamesTheTenantAndTheProvisioningStep()
    {
        _service.Status = 403;

        var ex = await Assert.ThrowsAsync<TenantTokenExchangeDeniedException>(() =>
            Run(TestJwt.For("root", Canary), "-tid", "child", "-q", "{ ok }"));

        Assert.Equal(TenantTokenDenialCause.Forbidden, ex.Cause);
        Assert.Contains("ProvisionCurrentUser -ttid child", ex.Message);
        Assert.DoesNotContain(Canary, ex.ToString());
    }

    private void AssertCanaryNowhere(string contextToken, string? extra = null)
    {
        var everything = string.Join('\n', _streams.Output.Concat(_streams.Error)
            .Concat(_logger.Entries.Select(e => e.Message)).Append(extra ?? string.Empty));
        Assert.DoesNotContain(Canary, everything);
        Assert.DoesNotContain(contextToken, everything);
        foreach (var header in _service.Authorization)
        {
            Assert.NotEqual($"Bearer {contextToken}", header);
        }
    }

    private static string TokenPayload(string authorizationHeader)
    {
        var payload = authorizationHeader["Bearer ".Length..].Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
    }

    private async Task Run(string contextToken, params string[] args)
    {
        var entry = new ContextEntry
        {
            OctoToolOptions = new OctoToolOptions { TenantId = "root" },
            Authentication = new OctoToolAuthenticationOptions
            {
                AccessToken = contextToken,
                RefreshToken = "refresh",
                AccessTokenExpiresAt = DateTime.Now.AddHours(1)
            }
        };
        var contexts = new StubContextManager(entry, "root-ctx");
        var provider = new TenantTokenProvider(_exchange, new ContextTenantTokenStore(contexts));
        var auth = new AuthenticationService(Options.Create(entry.Authentication), A.Fake<IAuthenticatorClient>(),
            contexts, provider);
        var client = new AssetServicesClient(
            new AssetServiceClientOptions { EndpointUri = _service.BaseUrl, TenantId = "root" },
            new AccessTokenHolder(), provider);
        var command = new GraphQlCommand(_logger, Options.Create(entry.OctoToolOptions), client, auth, _streams);
        command.CommandArgumentValue.ParseLayer(args);

        await command.PreValidate();
        await command.Execute();
    }

    private sealed class AccessTokenHolder :
        Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.ServiceClientAccessToken,
        IAssetServiceClientAccessToken;
}
