using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Ai;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.General.Authentication;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.IdentityProviders;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Users;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AiServices;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#8044 — every octo-cli command that takes a password or secret reads it from a hidden prompt, standard input or
///     an environment variable; the old command-line argument still works with a deprecation warning that never shows the
///     value. One test per command and source, against the real command class and a fake service client.
/// </summary>
public sealed class SecretArgumentCommandTests
{
    // Throwaway value, generated per test run.
    private readonly string _secret = "s-" + Guid.NewGuid().ToString("N");

    private readonly IOptions<OctoToolOptions> _options = Options.Create(new OctoToolOptions { TenantId = "acme" });
    private readonly IIdentityServicesClient _identity = A.Fake<IIdentityServicesClient>();
    private readonly IConfirmationService _confirm = A.Fake<IConfirmationService>();

    public SecretArgumentCommandTests()
    {
        A.CallTo(() => _confirm.Confirm(A<string>._)).Returns(true);
    }

    private static void AssertNoSecretIn<T>(RecordingLogger<T> logger, string secret) =>
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains(secret));

    private static void AssertDeprecationWarning<T>(RecordingLogger<T> logger, string argument) =>
        Assert.Contains(logger.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains(argument) && e.Message.Contains("deprecated"));

    // ------------------------------------------------------------------ ResetPassword

    [Fact]
    public async Task ResetPassword_FromStandardInput_WithoutAnyPasswordArgument()
    {
        string? sent = null;
        A.CallTo(() => _identity.ResetPassword(A<string>._, A<string>._))
            .Invokes((string _, string password) => sent = password);
        var console = new FakeSecretConsole { IsInputRedirected = true, StandardInput = _secret + "\n" };
        var logger = new RecordingLogger<ResetPassword>();
        var command = new ResetPassword(logger, _options, _identity, A.Fake<IAuthenticationService>(), _confirm,
            new SecretInput(console));
        command.CommandArgumentValue.ParseLayer(["-un", "alice", "--password-stdin", "-y"]);

        await command.Execute();

        Assert.Equal(_secret, sent);
        AssertNoSecretIn(logger, _secret);
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task ResetPassword_OnATerminal_PromptsTwice()
    {
        string? sent = null;
        A.CallTo(() => _identity.ResetPassword(A<string>._, A<string>._))
            .Invokes((string _, string password) => sent = password);
        var console = new FakeSecretConsole().Answer(_secret, _secret);
        var command = new ResetPassword(new RecordingLogger<ResetPassword>(), _options, _identity,
            A.Fake<IAuthenticationService>(), _confirm, new SecretInput(console));
        command.CommandArgumentValue.ParseLayer(["-un", "alice"]);

        await command.Execute();

        Assert.Equal(_secret, sent);
        Assert.Equal(2, console.Prompts.Count);
    }

    [Fact]
    public async Task ResetPassword_FromTheEnvironment()
    {
        string? sent = null;
        A.CallTo(() => _identity.ResetPassword(A<string>._, A<string>._))
            .Invokes((string _, string password) => sent = password);
        var console = new FakeSecretConsole { IsInputRedirected = true };
        console.Environment[Constants.EnvVarNewPassword] = _secret;
        var command = new ResetPassword(new RecordingLogger<ResetPassword>(), _options, _identity,
            A.Fake<IAuthenticationService>(), _confirm, new SecretInput(console));
        command.CommandArgumentValue.ParseLayer(["-un", "alice", "-y"]);

        await command.Execute();

        Assert.Equal(_secret, sent);
    }

    [Fact]
    public async Task ResetPassword_DeprecatedArgument_StillWorks_WithAWarning()
    {
        string? sent = null;
        A.CallTo(() => _identity.ResetPassword(A<string>._, A<string>._))
            .Invokes((string _, string password) => sent = password);
        var logger = new RecordingLogger<ResetPassword>();
        var command = new ResetPassword(logger, _options, _identity, A.Fake<IAuthenticationService>(), _confirm,
            FakeSecretConsole.Empty());
        command.CommandArgumentValue.ParseLayer(["-un", "alice", "-p", _secret, "-y"]);

        await command.Execute();

        Assert.Equal(_secret, sent);
        AssertDeprecationWarning(logger, "-p");
        AssertNoSecretIn(logger, _secret);
    }

    [Fact]
    public async Task ResetPassword_InAScriptWithoutAnySource_FailsBeforeAnyRequest()
    {
        var command = new ResetPassword(new RecordingLogger<ResetPassword>(), _options, _identity,
            A.Fake<IAuthenticationService>(), _confirm, FakeSecretConsole.Empty());
        command.CommandArgumentValue.ParseLayer(["-un", "alice", "-y"]);

        var ex = await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Contains(Constants.EnvVarNewPassword, ex.Message);
        A.CallTo(() => _identity.ResetPassword(A<string>._, A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ResetPassword_DeclinedConfirmation_ReadsNoPassword()
    {
        A.CallTo(() => _confirm.Confirm(A<string>._)).Returns(false);
        var console = new FakeSecretConsole().Answer(_secret, _secret);
        var command = new ResetPassword(new RecordingLogger<ResetPassword>(), _options, _identity,
            A.Fake<IAuthenticationService>(), _confirm, new SecretInput(console));
        command.CommandArgumentValue.ParseLayer(["-un", "alice"]);

        await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Empty(console.Prompts);
    }

    // ------------------------------------------------------------------ CreateUser / Setup

    [Fact]
    public async Task CreateUser_WithoutAnySource_CreatesTheUserWithoutPassword_AndDoesNotPrompt()
    {
        UserDto? sent = null;
        A.CallTo(() => _identity.CreateUser(A<UserDto>._)).Invokes((UserDto dto) => sent = dto);
        var console = new FakeSecretConsole();
        var command = new CreateUser(new RecordingLogger<CreateUser>(), _options, _identity,
            A.Fake<IAuthenticationService>(), new SecretInput(console));
        command.CommandArgumentValue.ParseLayer(["-un", "bob", "-e", "bob@example.com"]);

        await command.Execute();

        Assert.Null(Assert.IsType<RegisterUserDto>(sent).Password);
        Assert.Empty(console.Prompts);
    }

    [Fact]
    public async Task CreateUser_FromStandardInput()
    {
        UserDto? sent = null;
        A.CallTo(() => _identity.CreateUser(A<UserDto>._)).Invokes((UserDto dto) => sent = dto);
        var console = new FakeSecretConsole { IsInputRedirected = true, StandardInput = _secret };
        var logger = new RecordingLogger<CreateUser>();
        var command = new CreateUser(logger, _options, _identity, A.Fake<IAuthenticationService>(),
            new SecretInput(console));
        command.CommandArgumentValue.ParseLayer(["-un", "bob", "-e", "bob@example.com", "--password-stdin"]);

        await command.Execute();

        Assert.Equal(_secret, Assert.IsType<RegisterUserDto>(sent).Password);
        AssertNoSecretIn(logger, _secret);
    }

    [Fact]
    public async Task CreateUser_DeprecatedArgument_StillWorks_WithAWarning()
    {
        UserDto? sent = null;
        A.CallTo(() => _identity.CreateUser(A<UserDto>._)).Invokes((UserDto dto) => sent = dto);
        var logger = new RecordingLogger<CreateUser>();
        var command = new CreateUser(logger, _options, _identity, A.Fake<IAuthenticationService>(),
            FakeSecretConsole.Empty());
        command.CommandArgumentValue.ParseLayer(["-un", "bob", "-e", "bob@example.com", "-p", _secret]);

        await command.Execute();

        Assert.Equal(_secret, Assert.IsType<RegisterUserDto>(sent).Password);
        AssertDeprecationWarning(logger, "-p");
        AssertNoSecretIn(logger, _secret);
    }

    [Fact]
    public async Task Setup_FromTheEnvironment()
    {
        var setup = A.Fake<IIdentityServicesSetupClient>();
        AdminUserDto? sent = null;
        A.CallTo(() => setup.AddAdminUser(A<AdminUserDto>._)).Invokes((AdminUserDto dto) => sent = dto);
        var console = new FakeSecretConsole { IsInputRedirected = true };
        console.Environment[Constants.EnvVarNewPassword] = _secret;
        var command = new SetupCommand(new RecordingLogger<SetupCommand>(), _options, setup, new SecretInput(console));
        command.CommandArgumentValue.ParseLayer(["-e", "admin@example.com"]);

        await command.Execute();

        Assert.Equal(_secret, sent!.Password);
    }

    // ------------------------------------------------------------------ clients

    [Fact]
    public async Task AddClientCredentialsClient_OnATerminal_PromptsTwice_AndNeverReadsTheLoginSecret()
    {
        ClientDto? sent = null;
        A.CallTo(() => _identity.CreateClient(A<ClientDto>._)).Invokes((ClientDto dto) => sent = dto);
        var console = new FakeSecretConsole().Answer(_secret, _secret);
        console.Environment[Constants.EnvVarClientSecret] = "the-cli-login-secret";
        var command = new AddClientCredentialsClient(new RecordingLogger<AddClientCredentialsClient>(), _options,
            _identity, A.Fake<IAuthenticationService>(), new SecretInput(console));
        command.CommandArgumentValue.ParseLayer(["-id", "svc", "-n", "Service"]);

        await command.Execute();

        Assert.Equal(_secret, sent!.ClientSecret);
        Assert.Equal(2, console.Prompts.Count);
    }

    [Fact]
    public async Task AddClientCredentialsClient_FromTheEnvironment()
    {
        ClientDto? sent = null;
        A.CallTo(() => _identity.CreateClient(A<ClientDto>._)).Invokes((ClientDto dto) => sent = dto);
        var console = new FakeSecretConsole { IsInputRedirected = true };
        console.Environment[Constants.EnvVarNewClientSecret] = _secret;
        var command = new AddClientCredentialsClient(new RecordingLogger<AddClientCredentialsClient>(), _options,
            _identity, A.Fake<IAuthenticationService>(), new SecretInput(console));
        command.CommandArgumentValue.ParseLayer(["-id", "svc", "-n", "Service", "-apic"]);

        await command.Execute();

        Assert.Equal(_secret, sent!.ClientSecret);
        Assert.True(sent.AutoProvisionInChildTenants);
    }

    [Fact]
    public async Task AddDeviceCodeClient_FromStandardInput_AndTheDeprecatedArgumentWarns()
    {
        var sent = new List<ClientDto>();
        A.CallTo(() => _identity.CreateClient(A<ClientDto>._)).Invokes((ClientDto dto) => sent.Add(dto));

        var stdin = new AddDeviceCodeClient(new RecordingLogger<AddDeviceCodeClient>(), _options, _identity,
            A.Fake<IAuthenticationService>(),
            new SecretInput(new FakeSecretConsole { IsInputRedirected = true, StandardInput = _secret + "\n" }));
        stdin.CommandArgumentValue.ParseLayer(["-id", "dev", "-n", "Device", "--secret-stdin"]);
        await stdin.Execute();

        var logger = new RecordingLogger<AddDeviceCodeClient>();
        var legacy = new AddDeviceCodeClient(logger, _options, _identity, A.Fake<IAuthenticationService>(),
            FakeSecretConsole.Empty());
        legacy.CommandArgumentValue.ParseLayer(["-id", "dev2", "-n", "Device", "-s", _secret]);
        await legacy.Execute();

        Assert.All(sent, dto => Assert.Equal(_secret, dto.ClientSecret));
        Assert.Equal(2, sent.Count);
        AssertDeprecationWarning(logger, "-s");
        AssertNoSecretIn(logger, _secret);
    }

    // ------------------------------------------------------------------ identity providers

    [Fact]
    public async Task AddOAuthIdentityProvider_FromStandardInput()
    {
        IdentityProviderDto? sent = null;
        A.CallTo(() => _identity.CreateIdentityProvider(A<IdentityProviderDto>._))
            .Invokes((IdentityProviderDto dto) => sent = dto);
        var console = new FakeSecretConsole { IsInputRedirected = true, StandardInput = _secret };
        var command = new AddOAuthIdentityProvider(new RecordingLogger<AddOAuthIdentityProvider>(), _options,
            _identity, A.Fake<IAuthenticationService>(), new SecretInput(console));
        command.CommandArgumentValue.ParseLayer(["-n", "Google", "-t", "google", "-cid", "cid", "-e", "true",
            "--client-secret-stdin"]);

        await command.Execute();

        Assert.Equal(_secret, Assert.IsType<GoogleIdentityProviderDto>(sent).ClientSecret);
    }

    [Fact]
    public async Task AddAzureEntryIdIdentityProvider_FromTheEnvironment_AndTheDeprecatedArgumentWarns()
    {
        var sent = new List<AzureEntraIdProviderDto>();
        A.CallTo(() => _identity.CreateIdentityProvider(A<IdentityProviderDto>._))
            .Invokes((IdentityProviderDto dto) => sent.Add((AzureEntraIdProviderDto)dto));

        var console = new FakeSecretConsole { IsInputRedirected = true };
        console.Environment[Constants.EnvVarIdentityProviderClientSecret] = _secret;
        var fromEnvironment = new AddAzureEntryIdIdentityProvider(
            new RecordingLogger<AddAzureEntryIdIdentityProvider>(), _options, _identity,
            A.Fake<IAuthenticationService>(), new SecretInput(console));
        fromEnvironment.CommandArgumentValue.ParseLayer(["-n", "Azure", "-t", "tid", "-cid", "cid", "-e", "true"]);
        await fromEnvironment.Execute();

        var logger = new RecordingLogger<AddAzureEntryIdIdentityProvider>();
        var legacy = new AddAzureEntryIdIdentityProvider(logger, _options, _identity,
            A.Fake<IAuthenticationService>(), FakeSecretConsole.Empty());
        legacy.CommandArgumentValue.ParseLayer(["-n", "Azure2", "-t", "tid", "-cid", "cid", "-e", "true", "-cs",
            _secret]);
        await legacy.Execute();

        Assert.Equal(2, sent.Count);
        Assert.All(sent, dto => Assert.Equal(_secret, dto.ClientSecret));
        AssertDeprecationWarning(logger, "-cs");
        AssertNoSecretIn(logger, _secret);
    }

    [Fact]
    public async Task UpdateIdentityProvider_RotatesFromStandardInput_AndKeepsTheSecretWithoutASource()
    {
        A.CallTo(() => _identity.GetIdentityProvider(A<Meshmakers.Octo.ConstructionKit.Contracts.OctoObjectId>._))
            .Returns(new MicrosoftIdentityProviderDto { Name = "Old", ClientId = "cid" });
        var sent = new List<IdentityProviderDto>();
        A.CallTo(() => _identity.UpdateIdentityProvider(A<Meshmakers.Octo.ConstructionKit.Contracts.OctoObjectId>._,
                A<IdentityProviderDto>._))
            .Invokes((Meshmakers.Octo.ConstructionKit.Contracts.OctoObjectId _, IdentityProviderDto dto) => sent.Add(dto));
        const string providerId = "65f0a1b2c3d4e5f6a7b8c9d0";

        var rotate = new UpdateIdentityProvider(new RecordingLogger<UpdateIdentityProvider>(), _options, _identity,
            A.Fake<IAuthenticationService>(),
            new SecretInput(new FakeSecretConsole { IsInputRedirected = true, StandardInput = _secret }));
        rotate.CommandArgumentValue.ParseLayer(["-id", providerId, "-n", "New", "-e", "true", "--client-secret-stdin"]);
        await rotate.Execute();

        var keepConsole = new FakeSecretConsole();
        var keep = new UpdateIdentityProvider(new RecordingLogger<UpdateIdentityProvider>(), _options, _identity,
            A.Fake<IAuthenticationService>(), new SecretInput(keepConsole));
        keep.CommandArgumentValue.ParseLayer(["-id", providerId, "-n", "New", "-e", "true"]);
        await keep.Execute();

        Assert.Equal(_secret, Assert.IsType<MicrosoftIdentityProviderDto>(sent[0]).ClientSecret);
        Assert.Null(Assert.IsType<MicrosoftIdentityProviderDto>(sent[1]).ClientSecret);
        Assert.Empty(keepConsole.Prompts);
    }

    // ------------------------------------------------------------------ LogInClientCredentials

    private (LogInClientCredentialsCommand Command, IAuthenticatorClient Authenticator) LogIn(FakeSecretConsole console,
        RecordingLogger<LogInClientCredentialsCommand> logger)
    {
        var authenticator = A.Fake<IAuthenticatorClient>();
        A.CallTo(() => authenticator.RequestClientCredentialsTokenAsync(A<ApiScopes>._, A<DefaultScopes>._,
                A<IEnumerable<string>?>._, A<string?>._, A<string?>._))
            .Returns(new AuthenticationData());
        var command = new LogInClientCredentialsCommand(logger, _options, authenticator,
            A.Fake<IAuthenticationService>(), new SecretInput(console));
        return (command, authenticator);
    }

    [Fact]
    public async Task LogInClientCredentials_FromStandardInput()
    {
        var logger = new RecordingLogger<LogInClientCredentialsCommand>();
        var (command, authenticator) =
            LogIn(new FakeSecretConsole { IsInputRedirected = true, StandardInput = _secret + "\n" }, logger);
        command.CommandArgumentValue.ParseLayer(["-id", "ci", "--secret-stdin"]);

        await command.Execute();

        A.CallTo(() => authenticator.RequestClientCredentialsTokenAsync(A<ApiScopes>._, A<DefaultScopes>._,
            A<IEnumerable<string>?>._, "ci", _secret)).MustHaveHappenedOnceExactly();
        AssertNoSecretIn(logger, _secret);
    }

    [Fact]
    public async Task LogInClientCredentials_FromTheEnvironment_AsBefore()
    {
        var console = new FakeSecretConsole { IsInputRedirected = true };
        console.Environment[Constants.EnvVarClientSecret] = _secret;
        var (command, authenticator) = LogIn(console, new RecordingLogger<LogInClientCredentialsCommand>());
        command.CommandArgumentValue.ParseLayer(["-id", "ci"]);

        await command.Execute();

        A.CallTo(() => authenticator.RequestClientCredentialsTokenAsync(A<ApiScopes>._, A<DefaultScopes>._,
            A<IEnumerable<string>?>._, "ci", _secret)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task LogInClientCredentials_DeprecatedArgument_WinsOverTheEnvironment_WithAWarning()
    {
        var console = new FakeSecretConsole { IsInputRedirected = true };
        console.Environment[Constants.EnvVarClientSecret] = "from-the-environment";
        var logger = new RecordingLogger<LogInClientCredentialsCommand>();
        var (command, authenticator) = LogIn(console, logger);
        command.CommandArgumentValue.ParseLayer(["-id", "ci", "-s", _secret]);

        await command.Execute();

        A.CallTo(() => authenticator.RequestClientCredentialsTokenAsync(A<ApiScopes>._, A<DefaultScopes>._,
            A<IEnumerable<string>?>._, "ci", _secret)).MustHaveHappenedOnceExactly();
        AssertDeprecationWarning(logger, "-s");
        AssertNoSecretIn(logger, _secret);
    }

    [Fact]
    public async Task LogInClientCredentials_OnATerminal_PromptsOnce()
    {
        var console = new FakeSecretConsole().Answer(_secret);
        var (command, authenticator) = LogIn(console, new RecordingLogger<LogInClientCredentialsCommand>());
        command.CommandArgumentValue.ParseLayer(["-id", "ci"]);

        await command.Execute();

        Assert.Single(console.Prompts);
        A.CallTo(() => authenticator.RequestClientCredentialsTokenAsync(A<ApiScopes>._, A<DefaultScopes>._,
            A<IEnumerable<string>?>._, "ci", _secret)).MustHaveHappenedOnceExactly();
    }

    // ------------------------------------------------------------------ RedeemAiTicket

    private (RedeemAiTicketCommand Command, IAiServicesClient Client) Redeem(FakeSecretConsole console,
        RecordingLogger<RedeemAiTicketCommand> logger)
    {
        var client = A.Fake<IAiServicesClient>();
        A.CallTo(() => client.RedeemTicketAsync(A<string>._, A<string>._, A<string>._, A<string>._, A<DateTime>._,
                A<DateTime>._))
            .Returns(new CredentialsStatusDto());
        var command = new RedeemAiTicketCommand(logger, _options, client, A.Fake<IAuthenticationService>(), console);
        return (command, client);
    }

    [Fact]
    public async Task RedeemAiTicket_TokensFromTheEnvironment()
    {
        var console = new FakeSecretConsole();
        console.Environment[Constants.EnvVarAiAccessToken] = _secret + "-a";
        console.Environment[Constants.EnvVarAiRefreshToken] = _secret + "-r";
        var logger = new RecordingLogger<RedeemAiTicketCommand>();
        var (command, client) = Redeem(console, logger);
        command.CommandArgumentValue.ParseLayer(["-tid", "acme", "-tc", "CODE", "-aex", "2027-01-01T00:00:00Z",
            "-rex", "2027-12-31T00:00:00Z"]);

        await command.Execute();

        A.CallTo(() => client.RedeemTicketAsync("acme", "CODE", _secret + "-a", _secret + "-r", A<DateTime>._,
            A<DateTime>._)).MustHaveHappenedOnceExactly();
        AssertNoSecretIn(logger, _secret);
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task RedeemAiTicket_DeprecatedArguments_StillWork_WithAWarningEach()
    {
        var logger = new RecordingLogger<RedeemAiTicketCommand>();
        var (command, client) = Redeem(new FakeSecretConsole(), logger);
        command.CommandArgumentValue.ParseLayer(["-tid", "acme", "-tc", "CODE", "-at", _secret + "-a", "-rt",
            _secret + "-r", "-aex", "2027-01-01T00:00:00Z", "-rex", "2027-12-31T00:00:00Z"]);

        await command.Execute();

        A.CallTo(() => client.RedeemTicketAsync("acme", "CODE", _secret + "-a", _secret + "-r", A<DateTime>._,
            A<DateTime>._)).MustHaveHappenedOnceExactly();
        AssertDeprecationWarning(logger, "-at");
        AssertDeprecationWarning(logger, "-rt");
        AssertNoSecretIn(logger, _secret);
    }

    [Fact]
    public async Task RedeemAiTicket_OnlyOneTokenInTheEnvironment_IsRefused()
    {
        var console = new FakeSecretConsole();
        console.Environment[Constants.EnvVarAiAccessToken] = _secret;
        var (command, client) = Redeem(console, new RecordingLogger<RedeemAiTicketCommand>());
        command.CommandArgumentValue.ParseLayer(["-tid", "acme", "-tc", "CODE", "-aex", "2027-01-01T00:00:00Z",
            "-rex", "2027-12-31T00:00:00Z"]);

        var ex = await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Contains(Constants.EnvVarAiRefreshToken, ex.Message);
        Assert.DoesNotContain(_secret, ex.Message);
        A.CallTo(client).Where(c => c.Method.Name == nameof(IAiServicesClient.RedeemTicketAsync))
            .MustNotHaveHappened();
    }
}
