using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     Setup creates the first administrator of the system tenant or of another tenant: the tenant comes from
///     -tid, else from the context; the system tenant keeps its former call without a token.
/// </summary>
public sealed class SetupCommandTenantTests
{
    private const string AdminEMail = "admin@example.com";
    private const string IdentityServiceUrl = "https://identity.example.test/";

    private readonly string _password = "pw-" + Guid.NewGuid().ToString("N");
    private readonly IIdentityServicesSetupClient _systemSetupClient = A.Fake<IIdentityServicesSetupClient>();
    private readonly IIdentityServicesSetupClient _tenantSetupClient = A.Fake<IIdentityServicesSetupClient>();
    private readonly ServiceClientAccessToken _tenantClientToken = new();
    private readonly ITenantSetupClientFactory _tenantSetupClientFactory = A.Fake<ITenantSetupClientFactory>();
    private readonly IAuthenticationService _authenticationService = A.Fake<IAuthenticationService>();
    private readonly IChildTenantAccess _childTenantAccess = A.Fake<IChildTenantAccess>();

    public SetupCommandTenantTests()
    {
        A.CallTo(() => _tenantSetupClient.AccessToken).Returns(_tenantClientToken);
        A.CallTo(() => _tenantSetupClientFactory.Create(A<string>._)).Returns(_tenantSetupClient);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("octosystem")]
    [InlineData("OctoSystem")]
    [InlineData("system")]
    public async Task WithoutTenantArgument_OnTheSystemTenant_KeepsTheSystemCallWithoutAToken(string? contextTenantId)
    {
        var command = NewCommand(contextTenantId);
        command.CommandArgumentValue.ParseLayer(["-e", AdminEMail]);

        await command.Execute();

        A.CallTo(() => _systemSetupClient.AddAdminUser(
                A<AdminUserDto>.That.Matches(dto => dto.EMail == AdminEMail && dto.Password == _password)))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _tenantSetupClientFactory.Create(A<string>._)).MustNotHaveHappened();
        A.CallTo(() => _authenticationService.EnsureAuthenticated(A<IServiceClientAccessToken>._)).MustNotHaveHappened();
        A.CallTo(() => _childTenantAccess.GetAccessTokenAsync(A<string>._)).MustNotHaveHappened();
    }

    [Theory]
    [InlineData("octosystem")]
    [InlineData("SYSTEM")]
    public async Task TenantArgumentNamingTheSystemTenant_KeepsTheSystemCall(string tenantArgument)
    {
        var command = NewCommand("acme");
        command.CommandArgumentValue.ParseLayer(["-e", AdminEMail, "-tid", tenantArgument]);

        await command.Execute();

        A.CallTo(() => _systemSetupClient.AddAdminUser(A<AdminUserDto>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _tenantSetupClientFactory.Create(A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task WithoutTenantArgument_OnAChildTenantContext_SetsUpTheContextTenantWithTheContextToken()
    {
        A.CallTo(() => _authenticationService.EnsureAuthenticated(_tenantClientToken))
            .Invokes(() => _tenantClientToken.AccessToken = "token-of-acme");
        var command = NewCommand("acme");
        command.CommandArgumentValue.ParseLayer(["-e", AdminEMail]);

        await command.Execute();

        A.CallTo(() => _tenantSetupClientFactory.Create("acme")).MustHaveHappenedOnceExactly();
        A.CallTo(() => _tenantSetupClient.AddAdminUser(
                A<AdminUserDto>.That.Matches(dto => dto.EMail == AdminEMail && dto.Password == _password)))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _systemSetupClient.AddAdminUser(A<AdminUserDto>._)).MustNotHaveHappened();
        A.CallTo(() => _childTenantAccess.GetAccessTokenAsync(A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task TenantArgument_WinsOverTheContextTenant_AndUsesATokenOfThatTenant()
    {
        A.CallTo(() => _childTenantAccess.GetAccessTokenAsync("child1")).Returns("token-of-child1");
        var command = NewCommand("octosystem");
        command.CommandArgumentValue.ParseLayer(["-e", AdminEMail, "-tid", "Child1"]);

        await command.Execute();

        A.CallTo(() => _tenantSetupClientFactory.Create("child1")).MustHaveHappenedOnceExactly();
        Assert.Equal("token-of-child1", _tenantClientToken.AccessToken);
        A.CallTo(() => _tenantSetupClient.AddAdminUser(A<AdminUserDto>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _systemSetupClient.AddAdminUser(A<AdminUserDto>._)).MustNotHaveHappened();
        A.CallTo(() => _authenticationService.EnsureAuthenticated(A<IServiceClientAccessToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task TenantArgumentEqualToTheContextTenant_UsesTheContextToken()
    {
        A.CallTo(() => _authenticationService.EnsureAuthenticated(_tenantClientToken))
            .Invokes(() => _tenantClientToken.AccessToken = "token-of-acme");
        var command = NewCommand("acme");
        command.CommandArgumentValue.ParseLayer(["-e", AdminEMail, "-tid", "ACME"]);

        await command.Execute();

        A.CallTo(() => _tenantSetupClientFactory.Create("acme")).MustHaveHappenedOnceExactly();
        A.CallTo(() => _childTenantAccess.GetAccessTokenAsync(A<string>._)).MustNotHaveHappened();
        A.CallTo(() => _tenantSetupClient.AddAdminUser(A<AdminUserDto>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ChildTenant_WithoutAToken_IsRefusedBeforeTheCall()
    {
        var command = NewCommand("acme");
        command.CommandArgumentValue.ParseLayer(["-e", AdminEMail]);

        var exception = await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Contains("acme", exception.Message);
        A.CallTo(() => _tenantSetupClient.AddAdminUser(A<AdminUserDto>._)).MustNotHaveHappened();
        A.CallTo(() => _systemSetupClient.AddAdminUser(A<AdminUserDto>._)).MustNotHaveHappened();
    }

    [Theory]
    [InlineData("../octosystem")]
    [InlineData("a/b")]
    [InlineData("tenant?x=1")]
    [InlineData("tenant with blank")]
    public async Task MalformedTenantArgument_IsRefusedBeforeAnyCall(string tenantArgument)
    {
        var command = NewCommand("acme");
        command.CommandArgumentValue.ParseLayer(["-e", AdminEMail, "-tid", tenantArgument]);

        await Assert.ThrowsAsync<ToolException>(command.Execute);

        A.CallTo(() => _tenantSetupClientFactory.Create(A<string>._)).MustNotHaveHappened();
        A.CallTo(() => _systemSetupClient.AddAdminUser(A<AdminUserDto>._)).MustNotHaveHappened();
        A.CallTo(() => _childTenantAccess.GetAccessTokenAsync(A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public void TenantSetupClientFactory_AddressesTheTenantRoute()
    {
        var options = Options.Create(new OctoToolOptions { IdentityServiceUrl = IdentityServiceUrl });
        var factory = new TenantSetupClientFactory(options);

        var client = factory.Create("child1");

        Assert.Equal("https://identity.example.test/child1/v1", client.ServiceUri.ToString().TrimEnd('/'));
    }

    private SetupCommand NewCommand(string? contextTenantId)
    {
        var options = Options.Create(new OctoToolOptions { TenantId = contextTenantId });
        var console = new FakeSecretConsole { IsInputRedirected = true };
        console.Environment[Constants.EnvVarNewPassword] = _password;
        return new SetupCommand(new RecordingLogger<SetupCommand>(), options, _systemSetupClient,
            _tenantSetupClientFactory, _authenticationService, _childTenantAccess, new SecretInput(console));
    }
}
