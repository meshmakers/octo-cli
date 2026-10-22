using FakeItEasy;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Meshmakers.Octo.Sdk.ServiceClient.TenantLifecycle;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#7192 - an agent works in, and cleans up, the scratch tenants it creates without a human login:
///     <c>Create --scratch</c>, <c>Restore --scratch</c>, <c>Delete --disable-features</c> and the token for the child.
/// </summary>
public sealed class ScratchTenantCommandTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".tar.gz");

    public ScratchTenantCommandTests() => File.WriteAllText(_file, "payload");

    public void Dispose() => File.Delete(_file);

    // ---- Create --scratch -------------------------------------------------

    private static (CreateTenant Command, IAssetServicesClient Asset, IIdentityServicesClient Identity) NewCreate()
    {
        var asset = A.Fake<IAssetServicesClient>();
        var identity = A.Fake<IIdentityServicesClient>();
        A.CallTo(() => asset.AccessToken).Returns(A.Fake<IServiceClientAccessToken>());
        var command = new CreateTenant(NullLogger<CreateTenant>.Instance,
            Options.Create(new OctoToolOptions { TenantId = "parent1" }), asset, identity,
            A.Fake<IAuthenticationService>());
        return (command, asset, identity);
    }

    [Fact]
    public async Task Create_Scratch_CreatesAScratchTenant_AndProvisionsNoUser()
    {
        var (command, asset, identity) = NewCreate();
        command.CommandArgumentValue.ParseLayer(["-tid", "Scratch1", "-db", "Scratch1_Db", "-sc"]);

        await command.Execute();

        A.CallTo(() => asset.CreateTenantAsync("scratch1", "scratch1_db", true)).MustHaveHappenedOnceExactly();
        A.CallTo(() => asset.CreateTenantAsync("scratch1", "scratch1_db")).MustNotHaveHappened();
        // A service token has no user to provision; the creator's mirror is the administrator.
        A.CallTo(() => identity.ProvisionCurrentUser(A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Create_WithoutScratch_IsUnchanged_CreatesThePlainTenant_AndProvisionsTheUser()
    {
        var (command, asset, identity) = NewCreate();
        command.CommandArgumentValue.ParseLayer(["-tid", "plain1", "-db", "plain1_db"]);

        await command.Execute();

        A.CallTo(() => asset.CreateTenantAsync("plain1", "plain1_db")).MustHaveHappenedOnceExactly();
        A.CallTo(() => asset.CreateTenantAsync(A<string>._, A<string>._, A<bool>._)).MustNotHaveHappened();
        A.CallTo(() => identity.ProvisionCurrentUser("plain1")).MustHaveHappenedOnceExactly();
    }

    // ---- Restore --scratch ------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Restore_PassesTheScratchFlagOnlyWhenGiven(bool scratch)
    {
        var bot = A.Fake<IBotServicesClient>();
        A.CallTo(() => bot.RestoreRepositoryWithTusAsync(A<string>._, A<string>._, A<string>._, A<string?>._,
                A<bool>._, A<bool>._, A<Action<double>?>._, A<CancellationToken>._))
            .Returns(new JobResponseDto("job-1"));
        var command = new RestoreTenant(NullLogger<RestoreTenant>.Instance,
            Options.Create(new OctoToolOptions { TenantId = "octosystem" }), bot, A.Fake<IAuthenticationService>(),
            Options.Create(new BotServiceClientOptions()));
        command.CommandArgumentValue.ParseLayer(scratch
            ? ["-tid", "acme", "-db", "db", "-f", _file, "-sc"]
            : ["-tid", "acme", "-db", "db", "-f", _file]);

        await command.Execute();

        A.CallTo(() => bot.RestoreRepositoryWithTusAsync("acme", "db", _file, A<string?>._, false, scratch,
                A<Action<double>?>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    // ---- Delete --disable-features ----------------------------------------

    private sealed class DeleteFixture
    {
        public IAssetServicesClient Asset { get; } = A.Fake<IAssetServicesClient>();
        public IChildTenantAccess Access { get; } = A.Fake<IChildTenantAccess>();
        public IChildTenantFeaturesFactory Factory { get; } = A.Fake<IChildTenantFeaturesFactory>();
        public IChildTenantFeatures Features { get; } = A.Fake<IChildTenantFeatures>();
        public List<string> Calls { get; } = [];
        public List<string> Output { get; } = [];
        public DeleteTenant Command { get; }

        public DeleteFixture(TenantFeaturesStatusDto status, Func<TenantFeatureKind, Exception?>? failure = null)
        {
            var console = A.Fake<IConsoleService>();
            A.CallTo(() => console.WriteLine(A<string>._)).Invokes((string line) => Output.Add(line));
            A.CallTo(() => Access.GetAccessTokenAsync("scratch1")).Invokes(() => Calls.Add("token")).Returns("child-token");
            A.CallTo(() => Factory.Create("scratch1", "child-token")).Returns(Features);
            A.CallTo(() => Features.GetStatusAsync()).Invokes(() => Calls.Add("status")).Returns(status);
            Task Switch(TenantFeatureKind kind)
            {
                Calls.Add("disable " + kind);
                var error = failure?.Invoke(kind);
                return error == null ? Task.CompletedTask : Task.FromException(error);
            }

            A.CallTo(() => Features.Switches).Returns(new DelegateTenantFeatureSwitches(
                () => Switch(TenantFeatureKind.AiServices), () => Switch(TenantFeatureKind.Communication),
                () => Switch(TenantFeatureKind.Reporting), () => Switch(TenantFeatureKind.StreamData)));
            A.CallTo(() => Asset.DeleteTenantAsync("scratch1")).Invokes(() => Calls.Add("delete"));
            A.CallTo(() => Asset.AccessToken).Returns(A.Fake<IServiceClientAccessToken>());

            Command = new DeleteTenant(NullLogger<DeleteTenant>.Instance,
                Options.Create(new OctoToolOptions { TenantId = "parent1" }), Asset,
                A.Fake<IAuthenticationService>(), A.Fake<IConfirmationService>(), console, Access, Factory);
        }
    }

    private static TenantFeaturesStatusDto Enabled(bool ai, bool communication, bool reporting, bool streamData) => new()
    {
        AiServices = new TenantFeatureStatusDto { TenantEnabled = ai },
        Communication = new TenantFeatureStatusDto { TenantEnabled = communication },
        Reporting = new TenantFeatureStatusDto { TenantEnabled = reporting },
        StreamData = new StreamDataFeatureStatusDto { InstanceEnabled = true, TenantEnabled = streamData }
    };

    [Fact]
    public async Task Delete_WithoutDisableFeatures_IsUnchanged_NoChildTokenNoFeatureCalls()
    {
        var f = new DeleteFixture(Enabled(true, true, true, true));
        f.Command.CommandArgumentValue.ParseLayer(["-tid", "scratch1", "-y"]);

        await f.Command.Execute();

        Assert.Equal(["delete"], f.Calls);
        A.CallTo(() => f.Access.GetAccessTokenAsync(A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Delete_DisableFeatures_WithoutYes_IsRefused_BeforeAnyTokenOrCall()
    {
        var f = new DeleteFixture(Enabled(true, true, true, true));
        f.Command.CommandArgumentValue.ParseLayer(["-tid", "scratch1", "-df"]);

        var exception = await Assert.ThrowsAsync<ToolException>(() => f.Command.Execute());

        Assert.Contains("-y", exception.Message);
        Assert.Empty(f.Calls);
    }

    [Fact]
    public async Task Delete_DisableFeatures_SwitchesOffWhatIsEnabledWithTheChildToken_ThenDeletes_AndPrintsIt()
    {
        var f = new DeleteFixture(Enabled(true, true, false, true));
        f.Command.CommandArgumentValue.ParseLayer(["-tid", "Scratch1", "-df", "-y"]);

        await f.Command.Execute();

        Assert.Equal(
            ["token", "status", "disable AiServices", "disable Communication", "disable StreamData", "delete"],
            f.Calls);
        Assert.Equal(
        [
            "Disabled AI Services of tenant 'scratch1'.",
            "Disabled Communication of tenant 'scratch1'.",
            "Disabled Stream Data of tenant 'scratch1'."
        ], f.Output);
    }

    [Fact]
    public async Task Delete_DisableFeatures_NothingEnabled_SaysSo_AndDeletes()
    {
        var f = new DeleteFixture(Enabled(false, false, false, false));
        f.Command.CommandArgumentValue.ParseLayer(["-tid", "scratch1", "-df", "-y"]);

        await f.Command.Execute();

        Assert.Equal(["token", "status", "delete"], f.Calls);
        Assert.Contains("no capability is enabled", f.Output.Single());
    }

    [Fact]
    public async Task Delete_DisableFeatures_ActivatedArchives_AreReportedNotDeleted_AndTheTenantStaysUndeleted()
    {
        var f = new DeleteFixture(Enabled(false, true, false, true), kind => kind == TenantFeatureKind.StreamData
            ? new InvalidOperationException("Conflict: archives still activated: rawdata, hourly")
            : null);
        f.Command.CommandArgumentValue.ParseLayer(["-tid", "scratch1", "-df", "-y"]);

        var exception = await Assert.ThrowsAsync<ToolException>(() => f.Command.Execute());

        Assert.DoesNotContain("delete", f.Calls);
        Assert.Contains("Stream Data", exception.Message);
        Assert.Contains("never deleted", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(f.Output, line => line.Contains("Disabled Communication"));
        Assert.Contains(f.Output,
            line => line.Contains("Stream Data") && line.Contains("stays enabled") && line.Contains("rawdata, hourly"));
    }

    [Fact]
    public async Task Delete_DisableFeatures_NoChildToken_FailsBeforeTheTenantIsTouched()
    {
        var f = new DeleteFixture(Enabled(true, true, true, true));
        A.CallTo(() => f.Access.GetAccessTokenAsync("scratch1"))
            .Throws(new ToolException("No credentials to act in tenant 'scratch1'."));
        f.Command.CommandArgumentValue.ParseLayer(["-tid", "scratch1", "-df", "-y"]);

        await Assert.ThrowsAsync<ToolException>(() => f.Command.Execute());

        Assert.Empty(f.Calls);
    }

    // ---- the token for the child ------------------------------------------

    private static ChildTenantAccess NewAccess(IAuthenticatorClient authenticator, ITenantTokenProvider? provider,
        OctoToolAuthenticationOptions auth, Dictionary<string, string?> env) =>
        new(Options.Create(auth), authenticator, provider, name => env.GetValueOrDefault(name));

    private static Dictionary<string, string?> ClientEnv() => new()
    {
        [Constants.EnvVarClientId] = "agent-client",
        [Constants.EnvVarClientSecret] = "the-secret"
    };

    [Fact]
    public async Task ChildToken_ClientCredentialsSession_RequestsAClientCredentialsTokenForTheChild()
    {
        var authenticator = A.Fake<IAuthenticatorClient>();
        A.CallTo(() => authenticator.RequestClientCredentialsTokenForTenantAsync("scratch1", ApiScopes.OctoApiFullAccess,
                DefaultScopes.None, "agent-client", "the-secret"))
            .Returns(new AuthenticationData { AccessToken = "child-cc-token" });
        var provider = A.Fake<ITenantTokenProvider>();
        var access = NewAccess(authenticator, provider, new OctoToolAuthenticationOptions { AccessToken = "parent-token" },
            ClientEnv());

        var token = await access.GetAccessTokenAsync("scratch1");

        Assert.Equal("child-cc-token", token);
        A.CallTo(() => provider.GetAccessTokenAsync(A<string>._, A<string>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task ChildToken_WithTheMirrorsOwnSecret_UsesIt_NotTheInheritedParentSecret()
    {
        var authenticator = A.Fake<IAuthenticatorClient>();
        A.CallTo(() => authenticator.RequestClientCredentialsTokenForTenantAsync("scratch1", ApiScopes.OctoApiFullAccess,
                DefaultScopes.None, "agent-client", "the-mirror-secret"))
            .Returns(new AuthenticationData { AccessToken = "child-own-secret-token" });
        var env = ClientEnv();
        env[Constants.EnvVarChildClientSecret] = "the-mirror-secret";
        var access = NewAccess(authenticator, null, new OctoToolAuthenticationOptions { AccessToken = "parent-token" }, env);

        var token = await access.GetAccessTokenAsync("scratch1");

        Assert.Equal("child-own-secret-token", token);
        A.CallTo(() => authenticator.RequestClientCredentialsTokenForTenantAsync(A<string>._, A<ApiScopes>._,
            A<DefaultScopes>._, A<string>._, "the-secret")).MustNotHaveHappened();
    }

    [Fact]
    public async Task ChildToken_WithoutTheChildSecret_FallsBackToTheInheritedParentSecret()
    {
        var authenticator = A.Fake<IAuthenticatorClient>();
        A.CallTo(() => authenticator.RequestClientCredentialsTokenForTenantAsync("scratch1", ApiScopes.OctoApiFullAccess,
                DefaultScopes.None, "agent-client", "the-secret"))
            .Returns(new AuthenticationData { AccessToken = "child-inherited-token" });
        var access = NewAccess(authenticator, null, new OctoToolAuthenticationOptions { AccessToken = "parent-token" },
            ClientEnv());

        Assert.Equal("child-inherited-token", await access.GetAccessTokenAsync("scratch1"));
    }

    [Fact]
    public async Task ChildToken_AnEmptyChildSecret_IsTreatedAsNotSet()
    {
        var authenticator = A.Fake<IAuthenticatorClient>();
        A.CallTo(() => authenticator.RequestClientCredentialsTokenForTenantAsync("scratch1", ApiScopes.OctoApiFullAccess,
                DefaultScopes.None, "agent-client", "the-secret"))
            .Returns(new AuthenticationData { AccessToken = "child-inherited-token" });
        var env = ClientEnv();
        env[Constants.EnvVarChildClientSecret] = "";
        var access = NewAccess(authenticator, null, new OctoToolAuthenticationOptions { AccessToken = "parent-token" }, env);

        Assert.Equal("child-inherited-token", await access.GetAccessTokenAsync("scratch1"));
    }

    [Fact]
    public async Task ChildToken_TheChildSecretAlone_WithoutClientId_StillFailsWithTheRemedy()
    {
        var env = new Dictionary<string, string?> { [Constants.EnvVarChildClientSecret] = "the-mirror-secret" };
        var access = NewAccess(A.Fake<IAuthenticatorClient>(), null,
            new OctoToolAuthenticationOptions { AccessToken = "parent-token" }, env);

        var exception = await Assert.ThrowsAsync<ToolException>(() => access.GetAccessTokenAsync("scratch1"));

        Assert.Contains(Constants.EnvVarChildClientSecret, exception.Message);
    }

    [Fact]
    public async Task ChildToken_UserSession_ExchangesTheContextToken_NeverTheClientEnvironment()
    {
        var authenticator = A.Fake<IAuthenticatorClient>();
        var provider = A.Fake<ITenantTokenProvider>();
        A.CallTo(() => provider.GetAccessTokenAsync("scratch1", "user-token", A<CancellationToken>._))
            .Returns("child-exchanged");
        var access = NewAccess(authenticator, provider,
            new OctoToolAuthenticationOptions { AccessToken = "user-token", RefreshToken = "refresh" }, ClientEnv());

        var token = await access.GetAccessTokenAsync("scratch1");

        Assert.Equal("child-exchanged", token);
        A.CallTo(authenticator).Where(c => c.Method.Name == nameof(IAuthenticatorClient
            .RequestClientCredentialsTokenForTenantAsync)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ChildToken_ClientCredentialsSessionWithoutClientEnvironment_FailsAndNamesTheRemedy()
    {
        var access = NewAccess(A.Fake<IAuthenticatorClient>(), A.Fake<ITenantTokenProvider>(),
            new OctoToolAuthenticationOptions { AccessToken = "parent-token" }, []);

        var exception = await Assert.ThrowsAsync<ToolException>(() => access.GetAccessTokenAsync("scratch1"));

        Assert.Contains(Constants.EnvVarClientId, exception.Message);
        Assert.Contains("scratch1", exception.Message);
    }

    [Fact]
    public async Task ChildToken_TheIdentityServiceRefusesTheClient_NamesTheMirrorCondition_WithoutTheSecret()
    {
        var authenticator = A.Fake<IAuthenticatorClient>();
        A.CallTo(() => authenticator.RequestClientCredentialsTokenForTenantAsync(A<string>._, A<ApiScopes>._,
                A<DefaultScopes>._, A<string>._, A<string>._))
            .Throws(new AuthenticationFailedException("invalid_client"));
        var access = NewAccess(authenticator, null, new OctoToolAuthenticationOptions { AccessToken = "parent-token" },
            ClientEnv());

        var exception = await Assert.ThrowsAsync<ToolException>(() => access.GetAccessTokenAsync("scratch1"));

        Assert.Contains("AutoProvisionInChildTenants", exception.Message);
        Assert.DoesNotContain("the-secret", exception.Message);
    }
}
