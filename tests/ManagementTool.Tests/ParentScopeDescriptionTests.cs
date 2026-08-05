using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
/// The confirmation prompts of destructive commands that act on a child tenant name the parent
/// scope, because the child alone does not identify the operation: the parent comes from the
/// effective context, which --context can change per invocation. The prompt itself is only visible
/// on a real terminal (ConfirmationService suppresses it when input is redirected), so the scope
/// description is pinned here instead.
/// </summary>
public sealed class ParentScopeDescriptionTests
{
    [Fact]
    public void NamesParentTenantAndHost()
    {
        var command = NewCommand("octosystem", "https://localhost:5001/octosystem/v1");

        Assert.Equal("parent tenant 'octosystem' at 'https://localhost:5001'", command.Scope);
    }

    [Fact]
    public void DistinguishesEnvironmentsThatShareATenantId()
    {
        // The case a parent tenant alone would not catch: same tenant, different environment.
        var local = NewCommand("octosystem", "https://localhost:5001/octosystem/v1");
        var prod = NewCommand("octosystem", "https://assets.example.com/octosystem/v1");

        Assert.NotEqual(local.Scope, prod.Scope);
        Assert.Contains("https://assets.example.com", prod.Scope);
    }

    [Fact]
    public void WithoutTenantInContext_SaysSoInsteadOfLeavingItBlank()
    {
        var command = NewCommand(null, "https://localhost:5001/system/v1");

        Assert.Equal("parent tenant '<none>' at 'https://localhost:5001'", command.Scope);
    }

    private static TestCommand NewCommand(string? tenantId, string serviceUri) =>
        new(Options.Create(new OctoToolOptions { TenantId = tenantId }), new FakeServiceClient(serviceUri));

    /// <summary>Exposes the protected helper for assertions.</summary>
    private sealed class TestCommand : ServiceClientOctoCommand<IServiceClient>
    {
        public TestCommand(IOptions<OctoToolOptions> options, IServiceClient serviceClient)
            : base(NullLogger<ServiceClientOctoCommand<IServiceClient>>.Instance, "Test group", "Test",
                "Test command.", options, serviceClient, new FakeAuthenticationService())
        {
        }

        public string Scope => ParentScopeDescription;

        public override Task Execute() => Task.CompletedTask;
    }

    private sealed class FakeServiceClient(string serviceUri) : IServiceClient
    {
        public IServiceClientAccessToken AccessToken { get; } = new FakeAccessToken();
        public Uri ServiceUri { get; } = new(serviceUri);
    }

    private sealed class FakeAccessToken : IServiceClientAccessToken
    {
        public string? AccessToken { get; set; }

        // Never raised — nothing under test reads the token, the interface just requires it.
        public event EventHandler? AccessTokenUpdated
        {
            add { }
            remove { }
        }
    }

    private sealed class FakeAuthenticationService : IAuthenticationService
    {
        public Task EnsureAuthenticated(IServiceClientAccessToken serviceClientAccessToken) => Task.CompletedTask;

        public void SaveAuthenticationData(AuthenticationData authenticationData)
        {
        }
    }
}
