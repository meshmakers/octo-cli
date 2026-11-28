using System.Net;
using FakeItEasy;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Blueprints;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AiServices;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Blueprints;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#8452 part D, second call: until a starter tenant can be created with its blueprint in one call (AB#10065), the
///     CLI path is <c>Create</c> then <c>InstallBlueprint System.Ai.Starter-&lt;version&gt; --child-tenant-id</c> from the
///     system tenant, and that install sets the child's default LLM cap with the rules of <c>Create --blueprint</c>.
/// </summary>
public sealed class InstallBlueprintStarterCapCommandTests
{
    private const string Starter = "System.Ai.Starter-1.2.0";

    private sealed record Setup(
        InstallBlueprint Command,
        IAssetServicesClient ContextClient,
        IAssetServicesClient ChildClient,
        IChildTenantAssetClientFactory Factory,
        IAiServicesClient Ai,
        RecordingLogger<InstallBlueprint> Logger);

    private static Setup NewInstall(string contextTenantId = "octosystem", string? aiServiceUrl = "http://ai.local/",
        string outcome = StarterSpendCapResultDto.Created, string? refreshToken = "person-refresh",
        bool installSucceeds = true)
    {
        BlueprintApplyResultDto Result(string tenant, BlueprintApplyRequestDto request) => new()
        {
            Success = installSucceeds, BlueprintId = request.BlueprintId, TenantId = tenant
        };

        var contextClient = A.Fake<IAssetServicesClient>();
        A.CallTo(() => contextClient.ApplyBlueprintAsync(A<string>._, A<BlueprintApplyRequestDto>._))
            .ReturnsLazily((string t, BlueprintApplyRequestDto r) => Task.FromResult(Result(t, r)));
        var childClient = A.Fake<IAssetServicesClient>();
        A.CallTo(() => childClient.ApplyBlueprintAsync(A<string>._, A<BlueprintApplyRequestDto>._))
            .ReturnsLazily((string t, BlueprintApplyRequestDto r) => Task.FromResult(Result(t, r)));
        var factory = A.Fake<IChildTenantAssetClientFactory>();
        A.CallTo(() => factory.CreateAsync(A<string>._))
            .ReturnsLazily((string t) => Task.FromResult((t.Trim().ToLowerInvariant(), childClient)));

        var ai = A.Fake<IAiServicesClient>();
        A.CallTo(() => ai.ApplyStarterDefaultSpendCapAsync(A<string>._))
            .ReturnsLazily((string t) => Task.FromResult(new StarterSpendCapResultDto
            {
                TenantId = t, Outcome = outcome, KeySource = "CentralKey", CapMonthlyEur = 20m,
                Message = outcome == StarterSpendCapResultDto.NotConfigured ? "No default cap configured" : "ok"
            }));

        var logger = new RecordingLogger<InstallBlueprint>();
        var command = new InstallBlueprint(logger, A.Fake<IConsoleService>(),
            Options.Create(new OctoToolOptions { TenantId = contextTenantId, AiServiceUrl = aiServiceUrl }),
            contextClient, A.Fake<IAuthenticationService>(), ai,
            Options.Create(new OctoToolAuthenticationOptions { RefreshToken = refreshToken }), factory);
        return new Setup(command, contextClient, childClient, factory, ai, logger);
    }

    private static void NoCapCall(Setup s) =>
        A.CallTo(s.Ai).Where(c => c.Method.Name == nameof(IAiServicesClient.ApplyStarterDefaultSpendCapAsync))
            .MustNotHaveHappened();

    [Fact]
    public async Task StarterIntoAChild_FromTheSystemTenant_InstallsInTheChild_ThenSetsTheCap()
    {
        var s = NewInstall();
        s.Command.CommandArgumentValue.ParseLayer(["-b", Starter, "-ctid", "Starter1"]);

        await s.Command.Execute();

        A.CallTo(() => s.ChildClient.ApplyBlueprintAsync("starter1",
                A<BlueprintApplyRequestDto>.That.Matches(r => r.BlueprintId == Starter)))
            .MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => s.Ai.ApplyStarterDefaultSpendCapAsync("starter1")).MustHaveHappenedOnceExactly());
        A.CallTo(s.ContextClient).Where(c => c.Method.Name == nameof(IAssetServicesClient.ApplyBlueprintAsync))
            .MustNotHaveHappened();
        Assert.Contains(s.Logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("20"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task StarterIntoAChild_InAClientCredentialsSession_InstallsAndWarns_WithoutTheCall(
        string? refreshToken)
    {
        // No AI service URL either: the call is skipped anyway, so it must not stop the install.
        var s = NewInstall(aiServiceUrl: null, refreshToken: refreshToken);
        s.Command.CommandArgumentValue.ParseLayer(["-b", Starter, "-ctid", "scratch1"]);

        await s.Command.Execute();

        A.CallTo(() => s.ChildClient.ApplyBlueprintAsync("scratch1", A<BlueprintApplyRequestDto>._))
            .MustHaveHappenedOnceExactly();
        NoCapCall(s);
        var warning = Assert.Single(s.Logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("client-credentials session", warning.Message);
        Assert.Contains("SetAiSpendCap -t scratch1", warning.Message);
        Assert.Contains($"Blueprint '{Starter}' was installed into tenant 'scratch1'", warning.Message);
    }

    [Fact]
    public async Task CapExists_TheRepeatedInstallChangesNothing_AndNothingFails()
    {
        var s = NewInstall(outcome: StarterSpendCapResultDto.Exists);
        s.Command.CommandArgumentValue.ParseLayer(["-b", Starter, "-ctid", "starter1", "-f"]);

        await s.Command.Execute();

        A.CallTo(() => s.Ai.ApplyStarterDefaultSpendCapAsync("starter1")).MustHaveHappenedOnceExactly();
        Assert.Contains(s.Logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("not changed"));
        Assert.DoesNotContain(s.Logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task FailedInstall_SetsNoCap()
    {
        var s = NewInstall(installSucceeds: false);
        s.Command.CommandArgumentValue.ParseLayer(["-b", Starter, "-ctid", "starter1"]);

        await s.Command.Execute();

        NoCapCall(s);
        Assert.Contains(s.Logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task PersonLoginWithoutAiServiceUrl_StopsBeforeTheInstall(string? url)
    {
        var s = NewInstall(aiServiceUrl: url);
        s.Command.CommandArgumentValue.ParseLayer(["-b", Starter, "-ctid", "starter1"]);

        var ex = await Assert.ThrowsAsync<ToolException>(s.Command.Execute);

        Assert.Contains("AI service URL", ex.Message);
        A.CallTo(s.Factory).MustNotHaveHappened();
        A.CallTo(s.ChildClient).Where(c => c.Method.Name == nameof(IAssetServicesClient.ApplyBlueprintAsync))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task CapCallFails_KeepsTheError_SaysTheBlueprintIsInstalled_AndNamesTheRemedy()
    {
        var s = NewInstall();
        A.CallTo(() => s.Ai.ApplyStarterDefaultSpendCapAsync("starter1"))
            .ThrowsAsync(new ServiceClientResultException("Forbidden", HttpStatusCode.Forbidden, null));
        s.Command.CommandArgumentValue.ParseLayer(["-b", Starter, "-ctid", "starter1"]);

        await Assert.ThrowsAsync<ServiceClientResultException>(s.Command.Execute);

        var error = Assert.Single(s.Logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains($"Blueprint '{Starter}' was installed into tenant 'starter1'", error.Message);
        Assert.Contains("SetAiSpendCap -t starter1", error.Message);
    }

    [Fact]
    public async Task StarterIntoAChild_OfAnotherParent_SetsNoCap()
    {
        var s = NewInstall(contextTenantId: "customer1");
        s.Command.CommandArgumentValue.ParseLayer(["-b", Starter, "-ctid", "child1"]);

        await s.Command.Execute();

        A.CallTo(() => s.ChildClient.ApplyBlueprintAsync("child1", A<BlueprintApplyRequestDto>._))
            .MustHaveHappenedOnceExactly();
        NoCapCall(s);
    }

    [Fact]
    public async Task StarterWithoutChildTenantId_InstallsIntoTheContext_AndSetsNoCap()
    {
        var s = NewInstall(contextTenantId: "starter1");
        s.Command.CommandArgumentValue.ParseLayer(["-b", Starter]);

        await s.Command.Execute();

        A.CallTo(() => s.ContextClient.ApplyBlueprintAsync("starter1", A<BlueprintApplyRequestDto>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(s.Factory).MustNotHaveHappened();
        NoCapCall(s);
    }

    [Theory]
    [InlineData("Locations.Austria-1.3.0")]
    [InlineData("System.Ai.Builder-1.2.1")]
    public async Task OtherBlueprintIntoAChild_FromTheSystemTenant_SetsNoCap(string blueprint)
    {
        var s = NewInstall();
        s.Command.CommandArgumentValue.ParseLayer(["-b", blueprint, "-ctid", "child1"]);

        await s.Command.Execute();

        A.CallTo(() => s.ChildClient.ApplyBlueprintAsync("child1", A<BlueprintApplyRequestDto>._))
            .MustHaveHappenedOnceExactly();
        NoCapCall(s);
    }

    [Theory]
    [InlineData("octosystem")]
    [InlineData("unknown1")]
    public async Task ChildTenantAssetClientFactory_RefusesTheContextTenantAndANonChild_BeforeAnyToken(string tenant)
    {
        var asset = A.Fake<IAssetServicesClient>();
        A.CallTo(() => asset.ServiceUri).Returns(new Uri("https://asset.local/octosystem/v1"));
        A.CallTo(() => asset.GetTenantDescendantsAsync()).Returns(Task.FromResult<IEnumerable<TenantDto>>(
            [new TenantDto { TenantId = "starter1", Database = "starter1" }]));
        var access = A.Fake<IChildTenantAccess>();
        var factory = new ChildTenantAssetClientFactory(
            Options.Create(new OctoToolOptions { TenantId = "octosystem", AssetServiceUrl = "https://asset.local/" }),
            asset, A.Fake<IAuthenticationService>(), access);

        await Assert.ThrowsAsync<ToolException>(() => factory.CreateAsync(tenant));

        A.CallTo(access).MustNotHaveHappened();
    }

    [Fact]
    public async Task ChildTenantAssetClientFactory_ForAChild_UsesTheChildToken()
    {
        var asset = A.Fake<IAssetServicesClient>();
        A.CallTo(() => asset.GetTenantDescendantsAsync()).Returns(Task.FromResult<IEnumerable<TenantDto>>(
            [new TenantDto { TenantId = "starter1", Database = "starter1" }]));
        var access = A.Fake<IChildTenantAccess>();
        A.CallTo(() => access.GetAccessTokenAsync("starter1")).Returns(Task.FromResult("child-token"));
        var factory = new ChildTenantAssetClientFactory(
            Options.Create(new OctoToolOptions { TenantId = "octosystem", AssetServiceUrl = "https://asset.local/" }),
            asset, A.Fake<IAuthenticationService>(), access);

        var (tenantId, client) = await factory.CreateAsync(" Starter1 ");

        Assert.Equal("starter1", tenantId);
        Assert.Equal("child-token", client.AccessToken.AccessToken);
    }

    [Fact]
    public void Help_NamesTheChildTenantOption_AndTheStarterCap()
    {
        var s = NewInstall();

        var notes = s.Command.GetDocumentation()?.Notes;

        Assert.NotNull(notes);
        Assert.Contains(notes, n => n.Contains("--child-tenant-id"));
        var note = Assert.Single(notes, n => n.Contains("starterDefault"));
        Assert.Contains("client-credentials session", note);
        Assert.Contains("Other blueprints", note);
    }
}
