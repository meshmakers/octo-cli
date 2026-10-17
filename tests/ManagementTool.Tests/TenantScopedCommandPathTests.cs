using FakeItEasy;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Blueprints;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.CkModelLibraries;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#6836, the real command path: command, authentication service and asset services client are the
///     production classes, only identity (a counting fake) and the asset repository (a loopback listener) are
///     replaced. The request is observed on the wire, so a context token attached to a request for another
///     tenant cannot go unnoticed.
/// </summary>
public sealed class TenantScopedCommandPathTests : IDisposable
{
    private const string Child = "child";
    private readonly CountingExchange _exchange = new();
    private readonly WireService _service = new();

    public void Dispose() => _service.Dispose();

    [Fact]
    public async Task LibraryStatus_OnAChildContextWithARootToken_SendsOnlyTheChildToken()
    {
        var root = TestJwt.For("root", "root-token");
        var command = Wire(root, out _, c => new LibraryStatusCommand(NullLogger<LibraryStatusCommand>.Instance,
            Options.Create(OptionsFor(Child)), c.Client, c.Auth));

        await Run(command);

        Assert.Equal(1, _exchange.Calls);
        AssertOnlyChildTokenSent();
        Assert.Equal($"GET /{Child}/v1/models/LibraryStatus", _service.Requests.Single());
    }

    [Fact]
    public async Task ListBlueprintInstallations_OnAChildContextWithARootToken_SendsOnlyTheChildToken()
    {
        _service.Body = "[]";
        var root = TestJwt.For("root", "root-token");
        var command = Wire(root, out _, c => new ListBlueprintInstallations(
            NullLogger<ListBlueprintInstallations>.Instance, A.Fake<IConsoleService>(),
            Options.Create(OptionsFor(Child)), c.Client, c.Auth));

        await Run(command);

        AssertOnlyChildTokenSent();
        Assert.Equal($"GET /{Child}/v1/blueprints/installations", _service.Requests.Single());
    }

    [Fact]
    public async Task ClearCache_OnAChildContextWithARootToken_SendsOnlyTheChildToken()
    {
        _service.Body = "{}";
        var root = TestJwt.For("root", "root-token");
        var command = Wire(root, out _, c => new ClearTenantCache(NullLogger<ClearTenantCache>.Instance,
            Options.Create(OptionsFor(Child)), c.Client, c.Auth, A.Fake<IConfirmationService>()));
        command.CommandArgumentValue.ParseLayer(["-tid", "grandchild", "-y"]);

        await Run(command);

        AssertOnlyChildTokenSent();
        Assert.StartsWith($"PUT /{Child}/v1/tenants/clearCache", _service.Requests.Single());
    }

    [Fact]
    public async Task LibraryStatus_NotProvisioned_FailsBeforeAnyRequest_AndNeverSendsTheRootToken()
    {
        _exchange.Deny = TenantTokenDenialCause.NotProvisioned;
        var root = TestJwt.For("root", "root-token");
        var command = Wire(root, out _, c => new LibraryStatusCommand(NullLogger<LibraryStatusCommand>.Instance,
            Options.Create(OptionsFor(Child)), c.Client, c.Auth));

        var ex = await Assert.ThrowsAsync<TenantTokenExchangeDeniedException>(() => Run(command));

        Assert.Contains($"ProvisionCurrentUser -ttid {Child}", ex.Message);
        Assert.Empty(_service.Requests);
        Assert.Empty(_service.Authorization);
    }

    [Fact]
    public async Task LibraryStatus_OnTheContextOfTheTokenTenant_BehavesAsToday()
    {
        var token = TestJwt.For(Child, "own-token");
        var command = Wire(token, out _, c => new LibraryStatusCommand(NullLogger<LibraryStatusCommand>.Instance,
            Options.Create(OptionsFor(Child)), c.Client, c.Auth));

        await Run(command);

        Assert.Equal(0, _exchange.Calls);
        Assert.Equal($"Bearer {token}", _service.Authorization.Single());
    }

    [Fact]
    public async Task Forbidden_FromTheService_NamesTheTenantAndTheProvisioningStep()
    {
        _service.Status = 403;
        var command = Wire(TestJwt.For(Child), out _, c => new LibraryStatusCommand(
            NullLogger<LibraryStatusCommand>.Instance, Options.Create(OptionsFor(Child)), c.Client, c.Auth));

        var ex = await Assert.ThrowsAsync<TenantTokenExchangeDeniedException>(() => Run(command));

        Assert.Equal(TenantTokenDenialCause.Forbidden, ex.Cause);
        Assert.Contains($"ProvisionCurrentUser -ttid {Child}", ex.Message);
    }

    private static async Task Run(Meshmakers.Common.CommandLineParser.Commands.ICommand command)
    {
        await command.PreValidate();
        await command.Execute();
    }

    private void AssertOnlyChildTokenSent()
    {
        var sent = Assert.Single(_service.Authorization);
        Assert.Equal(Child, AccessTokenInspector.TryGetTenantId(sent["Bearer ".Length..]));
        Assert.DoesNotContain("root", sent);
    }

    private static OctoToolOptions OptionsFor(string tenant) => new() { TenantId = tenant };

    private T Wire<T>(string contextToken, out object logger, Func<(IAssetServicesClient Client, IAuthenticationService Auth), T> create)
    {
        logger = new object();
        var entry = new ContextEntry
        {
            OctoToolOptions = OptionsFor(Child),
            Authentication = new OctoToolAuthenticationOptions
            {
                AccessToken = contextToken,
                RefreshToken = "refresh",
                AccessTokenExpiresAt = DateTime.Now.AddHours(1)
            }
        };
        var contexts = new StubContextManager(entry);
        var provider = new TenantTokenProvider(_exchange, new ContextTenantTokenStore(contexts));
        var auth = new AuthenticationService(Options.Create(entry.Authentication), A.Fake<IAuthenticatorClient>(),
            contexts, provider);
        var token = new ServiceClientAccessTokenForTests();
        var client = new AssetServicesClient(
            new AssetServiceClientOptions { EndpointUri = _service.BaseUrl, TenantId = Child }, token, provider);
        return create((client, auth));
    }

    private sealed class ServiceClientAccessTokenForTests : Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.ServiceClientAccessToken,
        IAssetServiceClientAccessToken;
}
