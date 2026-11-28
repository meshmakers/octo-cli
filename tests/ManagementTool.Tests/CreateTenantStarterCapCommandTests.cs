using System.Net;
using FakeItEasy;
using GraphQL;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AiServices;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#8452 part D: <c>Create --blueprint System.Ai.Starter-&lt;version&gt;</c> run from the system tenant gives the
///     new tenant the cluster's default LLM cap (AI service <c>starterDefault</c>), as MCP
///     <c>create_tenant starter=true</c> does. Other blueprints, other parent tenants and tenants without a blueprint
///     get no cap call.
/// </summary>
public sealed class CreateTenantStarterCapCommandTests
{
    private const string Starter = "System.Ai.Starter-1.2.0";

    private sealed record Setup(
        CreateTenant Command,
        IAssetServicesClient Asset,
        ITenantClient Tenant,
        IAiServicesClient Ai,
        List<GraphQLRequest> Requests,
        RecordingLogger<CreateTenant> Logger);

    private static Setup NewCreate(string parentTenantId = "octosystem", string? aiServiceUrl = "http://ai.local/",
        string outcome = StarterSpendCapResultDto.Created, string? refreshToken = "person-refresh")
    {
        var asset = A.Fake<IAssetServicesClient>();
        A.CallTo(() => asset.AccessToken).Returns(A.Fake<IServiceClientAccessToken>());
        var requests = new List<GraphQLRequest>();
        var tenant = MethodFakes.IdentityTenant(requests);
        var ai = A.Fake<IAiServicesClient>();
        A.CallTo(() => ai.ApplyStarterDefaultSpendCapAsync(A<string>._))
            .ReturnsLazily((string t) => Task.FromResult(new StarterSpendCapResultDto
            {
                TenantId = t, Outcome = outcome, KeySource = "CentralKey", CapMonthlyEur = 20m,
                Message = outcome == StarterSpendCapResultDto.NotConfigured ? "No default cap configured" : "ok"
            }));
        var logger = new RecordingLogger<CreateTenant>();
        var command = new CreateTenant(logger,
            Options.Create(new OctoToolOptions { TenantId = parentTenantId, AiServiceUrl = aiServiceUrl }), asset,
            tenant, ai, A.Fake<IAuthenticationService>(),
            Options.Create(new OctoToolAuthenticationOptions { RefreshToken = refreshToken }));
        return new Setup(command, asset, tenant, ai, requests, logger);
    }

    [Fact]
    public async Task StarterFromSystemTenant_SetsTheDefaultCap_AfterCreate_BeforeProvisioning()
    {
        var s = NewCreate();
        s.Command.CommandArgumentValue.ParseLayer(["-tid", "Starter1", "-b", Starter]);

        await s.Command.Execute();

        A.CallTo(() => s.Asset.CreateTenantAsync("starter1", null, false, Starter)).MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => s.Ai.ApplyStarterDefaultSpendCapAsync("starter1")).MustHaveHappenedOnceExactly())
            .Then(A.CallTo(s.Tenant).Where(c => c.Method.Name == nameof(ITenantClient.SendMutationAsync))
                .MustHaveHappenedOnceExactly());
        Assert.Contains(s.Logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("20"));
    }

    [Theory]
    [InlineData("OctoSystem", "system.ai.starter-1.2.0")]
    [InlineData(" system ", "SYSTEM.AI.STARTER-1.3.0")]
    public async Task StarterFromSystemTenant_IgnoresCase(string parent, string blueprint)
    {
        var s = NewCreate(parent);
        s.Command.CommandArgumentValue.ParseLayer(["-tid", "s2", "-np", "-b", blueprint]);

        await s.Command.Execute();

        A.CallTo(() => s.Ai.ApplyStarterDefaultSpendCapAsync("s2")).MustHaveHappenedOnceExactly();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task StarterFromSystemTenant_InAClientCredentialsSession_SkipsTheCall_AndNamesTheRemedy(
        string? refreshToken)
    {
        // The endpoint takes only person tokens; a client session would be refused after the tenant exists.
        var s = NewCreate(refreshToken: refreshToken);
        s.Command.CommandArgumentValue.ParseLayer(["-tid", "scratch1", "-sc", "-b", Starter]);

        await s.Command.Execute();

        A.CallTo(() => s.Asset.CreateTenantAsync("scratch1", null, true, Starter)).MustHaveHappenedOnceExactly();
        A.CallTo(s.Ai).Where(c => c.Method.Name == nameof(IAiServicesClient.ApplyStarterDefaultSpendCapAsync))
            .MustNotHaveHappened();
        var warning = Assert.Single(s.Logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("SetAiSpendCap -t scratch1", warning.Message);
        Assert.Empty(s.Requests);
    }

    [Fact]
    public async Task StarterScratchFromSystemTenant_WithAPersonLogin_SetsTheCap_AndProvisionsNoUser()
    {
        var s = NewCreate();
        s.Command.CommandArgumentValue.ParseLayer(["-tid", "scratch1", "-sc", "-b", Starter]);

        await s.Command.Execute();

        A.CallTo(() => s.Ai.ApplyStarterDefaultSpendCapAsync("scratch1")).MustHaveHappenedOnceExactly();
        Assert.Empty(s.Requests);
    }

    [Fact]
    public async Task StarterFromAChildTenant_SetsNoCap()
    {
        var s = NewCreate("customer1");
        s.Command.CommandArgumentValue.ParseLayer(["-tid", "child1", "-b", Starter]);

        await s.Command.Execute();

        A.CallTo(() => s.Asset.CreateTenantAsync("child1", null, false, Starter)).MustHaveHappenedOnceExactly();
        A.CallTo(s.Ai).Where(c => c.Method.Name == nameof(IAiServicesClient.ApplyStarterDefaultSpendCapAsync))
            .MustNotHaveHappened();
    }

    [Theory]
    [InlineData("Locations.Austria-1.3.0")]
    [InlineData("System.Ai.Builder-1.2.1")]
    [InlineData("System.Ai.StarterKit-1.0.0")]
    public async Task OtherBlueprintFromSystemTenant_SetsNoCap(string blueprint)
    {
        var s = NewCreate();
        s.Command.CommandArgumentValue.ParseLayer(["-tid", "other1", "-b", blueprint]);

        await s.Command.Execute();

        A.CallTo(s.Ai).Where(c => c.Method.Name == nameof(IAiServicesClient.ApplyStarterDefaultSpendCapAsync))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task NoBlueprintFromSystemTenant_SetsNoCap()
    {
        var s = NewCreate();
        s.Command.CommandArgumentValue.ParseLayer(["-tid", "plain1"]);

        await s.Command.Execute();

        A.CallTo(() => s.Asset.CreateTenantAsync("plain1", null)).MustHaveHappenedOnceExactly();
        A.CallTo(s.Ai).Where(c => c.Method.Name == nameof(IAiServicesClient.ApplyStarterDefaultSpendCapAsync))
            .MustNotHaveHappened();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task StarterFromSystemTenant_WithoutAiServiceUrl_StopsBeforeTheTenantIsCreated(string? url)
    {
        var s = NewCreate(aiServiceUrl: url);
        s.Command.CommandArgumentValue.ParseLayer(["-tid", "starter1", "-b", Starter]);

        var ex = await Assert.ThrowsAsync<ToolException>(s.Command.Execute);

        Assert.Contains("AI service URL", ex.Message);
        A.CallTo(s.Asset).Where(c => c.Method.Name == nameof(IAssetServicesClient.CreateTenantAsync))
            .MustNotHaveHappened();
        Assert.Empty(s.Requests);
    }

    [Fact]
    public async Task StarterFromSystemTenant_ClientCredentialsWithoutAiServiceUrl_CreatesTheTenant_AndWarns()
    {
        // The call is skipped in a client session anyway, so a missing AI service URL must not stop the create.
        var s = NewCreate(aiServiceUrl: null, refreshToken: null);
        s.Command.CommandArgumentValue.ParseLayer(["-tid", "scratch1", "-sc", "-b", Starter]);

        await s.Command.Execute();

        A.CallTo(() => s.Asset.CreateTenantAsync("scratch1", null, true, Starter)).MustHaveHappenedOnceExactly();
        A.CallTo(s.Ai).Where(c => c.Method.Name == nameof(IAiServicesClient.ApplyStarterDefaultSpendCapAsync))
            .MustNotHaveHappened();
        var warning = Assert.Single(s.Logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("client-credentials session", warning.Message);
    }

    [Fact]
    public async Task CapCallFails_KeepsTheError_SaysTheTenantExists_AndNamesTheRemedies()
    {
        var s = NewCreate();
        A.CallTo(() => s.Ai.ApplyStarterDefaultSpendCapAsync("starter1"))
            .ThrowsAsync(new ServiceClientResultException("Forbidden", HttpStatusCode.Forbidden, null));
        s.Command.CommandArgumentValue.ParseLayer(["-tid", "starter1", "-b", Starter]);

        await Assert.ThrowsAsync<ServiceClientResultException>(s.Command.Execute);

        A.CallTo(() => s.Asset.CreateTenantAsync("starter1", null, false, Starter)).MustHaveHappenedOnceExactly();
        var error = Assert.Single(s.Logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains("Tenant 'starter1' was created", error.Message);
        Assert.Contains("SetAiSpendCap -t starter1", error.Message);
        Assert.Contains("ProvisionCurrentUser -ttid starter1", error.Message);
        Assert.Empty(s.Requests);
    }

    [Fact]
    public async Task CapExists_IsReported_AndNothingFails()
    {
        var s = NewCreate(outcome: StarterSpendCapResultDto.Exists);
        s.Command.CommandArgumentValue.ParseLayer(["-tid", "starter1", "-np", "-b", Starter]);

        await s.Command.Execute();

        Assert.Contains(s.Logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("not changed"));
        Assert.DoesNotContain(s.Logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task CapNotConfigured_WarnsWithTheServiceMessage()
    {
        var s = NewCreate(outcome: StarterSpendCapResultDto.NotConfigured);
        s.Command.CommandArgumentValue.ParseLayer(["-tid", "starter1", "-np", "-b", Starter]);

        await s.Command.Execute();

        var warning = Assert.Single(s.Logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("No default cap configured", warning.Message);
    }

    [Theory]
    [InlineData("System.Ai.Starter-1.2.0", true)]
    [InlineData("system.ai.starter-2.0.0", true)]
    [InlineData("System.Ai.Starter-", false)]
    [InlineData("System.Ai.Starter", false)]
    [InlineData("System.Ai.StarterKit-1.0.0", false)]
    [InlineData("Octo.Starter-1.2.0", false)]
    public void IsStarterBlueprint_MatchesOnlyTheStarterWithAVersion(string blueprintId, bool expected) =>
        Assert.Equal(expected, CreateTenant.IsStarterBlueprint(blueprintId));

    [Fact]
    public void Help_NamesTheStarterCap()
    {
        var s = NewCreate();

        var notes = s.Command.GetDocumentation()?.Notes;

        Assert.NotNull(notes);
        var note = Assert.Single(notes, n => n.Contains("starterDefault"));
        Assert.Contains("system tenant", note);
        Assert.Contains("Other blueprints", note);
        Assert.Contains("client-credentials session", note);
    }
}
