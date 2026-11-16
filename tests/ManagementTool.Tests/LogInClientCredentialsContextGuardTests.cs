using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.General.Authentication;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#9063 — <c>LogInClientCredentials --context local_octosystem</c> matched the stored context
///     <c>local_OctoSystem</c> case-insensitively and replaced its interactive login with a client-credentials token,
///     without a word. The command now refuses, names the matched context and changes nothing when the name matched only
///     by case or the context holds an interactive login, unless <c>--replace-login</c> is passed. Runs against a real
///     <see cref="ContextManager" /> in a throwaway directory, never the developer's ~/.octo-cli.
/// </summary>
public sealed class LogInClientCredentialsContextGuardTests : IDisposable
{
    private const string StoredName = "local_OctoSystem";

    // Throwaway values, generated per test run.
    private readonly string _secret = "s-" + Guid.NewGuid().ToString("N");
    private readonly string _refreshToken = "r-" + Guid.NewGuid().ToString("N");

    private readonly string _baseDirectory;
    private readonly IAuthenticatorClient _authenticator = A.Fake<IAuthenticatorClient>();
    private readonly IAuthenticationService _authenticationService = A.Fake<IAuthenticationService>();

    public LogInClientCredentialsContextGuardTests()
    {
        _baseDirectory = Path.Combine(Path.GetTempPath(), "octo-cli-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_baseDirectory);

        A.CallTo(() => _authenticator.RequestClientCredentialsTokenAsync(A<ApiScopes>._, A<DefaultScopes>._,
                A<IEnumerable<string>?>._, A<string?>._, A<string?>._))
            .Returns(new AuthenticationData());
    }

    public void Dispose()
    {
        if (Directory.Exists(_baseDirectory))
        {
            Directory.Delete(_baseDirectory, recursive: true);
        }
    }

    private ContextManager StoreContext(bool interactiveLogin)
    {
        var writer = new ContextManager(_baseDirectory);
        writer.AddOrUpdateContext(StoredName, new ContextEntry
        {
            OctoToolOptions = new OctoToolOptions { TenantId = "octosystem" },
            Authentication = new OctoToolAuthenticationOptions
            {
                AccessToken = "a-" + Guid.NewGuid().ToString("N"),
                RefreshToken = interactiveLogin ? _refreshToken : null
            }
        });

        // A fresh manager, as in a new octo-cli process.
        var manager = new ContextManager(_baseDirectory);
        manager.Load();
        return manager;
    }

    private LogInClientCredentialsCommand NewCommand(IContextManager contextManager,
        RecordingLogger<LogInClientCredentialsCommand> logger)
    {
        var console = new FakeSecretConsole { IsInputRedirected = true };
        console.Environment[Constants.EnvVarClientSecret] = _secret;

        return new LogInClientCredentialsCommand(logger,
            Options.Create(new OctoToolOptions { TenantId = "octosystem" }), _authenticator, _authenticationService,
            new SecretInput(console), contextManager);
    }

    private void AssertNothingChanged()
    {
        A.CallTo(() => _authenticator.RequestClientCredentialsTokenAsync(A<ApiScopes>._, A<DefaultScopes>._,
            A<IEnumerable<string>?>._, A<string?>._, A<string?>._)).MustNotHaveHappened();
        A.CallTo(() => _authenticationService.SaveAuthenticationData(A<AuthenticationData>._)).MustNotHaveHappened();

        var reader = new ContextManager(_baseDirectory);
        reader.Load();
        var stored = Assert.Single(reader.ListContexts());
        Assert.Equal(StoredName, stored.Key);
    }

    private void AssertLoggedIn()
    {
        A.CallTo(() => _authenticator.RequestClientCredentialsTokenAsync(A<ApiScopes>._, A<DefaultScopes>._,
            A<IEnumerable<string>?>._, "ci", _secret)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _authenticationService.SaveAuthenticationData(A<AuthenticationData>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public void SelectContext_KeepsTheRequestedNameNextToTheStoredOne()
    {
        var manager = StoreContext(interactiveLogin: false);
        Assert.Null(manager.RequestedContextName);

        manager.SelectContext("local_octosystem");

        Assert.Equal("local_octosystem", manager.RequestedContextName);
        Assert.Equal(StoredName, manager.GetEffectiveContextName());
    }

    [Fact]
    public async Task CaseOnlyMatch_OverAnInteractiveLogin_RefusesAndNamesTheStoredContext()
    {
        // The reported case.
        var manager = StoreContext(interactiveLogin: true);
        manager.SelectContext("local_octosystem");
        var logger = new RecordingLogger<LogInClientCredentialsCommand>();
        var command = NewCommand(manager, logger);
        command.CommandArgumentValue.ParseLayer(["-id", "ci"]);

        var exception = await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Contains("'local_octosystem'", exception.Message);
        Assert.Contains($"'{StoredName}'", exception.Message);
        Assert.Contains("only by case", exception.Message);
        Assert.Contains("interactive login", exception.Message);
        Assert.Contains("--replace-login", exception.Message);
        Assert.Contains("Nothing was changed", exception.Message);
        Assert.DoesNotContain(_refreshToken, exception.Message);
        AssertNothingChanged();
    }

    [Fact]
    public async Task CaseOnlyMatch_FromTheEnvironmentVariable_Refuses()
    {
        // OCTO_CLI_CONTEXT goes through the same startup path as --context: ContextSelection (trimmed), then SelectContext.
        var manager = StoreContext(interactiveLogin: true);
        var selection = ContextSelection.Resolve(["octo-cli", "-c", "LogInClientCredentials"],
            name => name == "OCTO_CLI_CONTEXT" ? " local_octosystem " : null);
        manager.SelectContext(selection.Name!);
        var command = NewCommand(manager, new RecordingLogger<LogInClientCredentialsCommand>());
        command.CommandArgumentValue.ParseLayer(["-id", "ci"]);

        var exception = await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Contains("'local_octosystem' matched the existing context 'local_OctoSystem' only by case",
            exception.Message);
        AssertNothingChanged();
    }

    [Fact]
    public async Task CaseOnlyMatch_OverAClientCredentialsLogin_Refuses()
    {
        var manager = StoreContext(interactiveLogin: false);
        manager.SelectContext("LOCAL_OCTOSYSTEM");
        var command = NewCommand(manager, new RecordingLogger<LogInClientCredentialsCommand>());
        command.CommandArgumentValue.ParseLayer(["-id", "ci"]);

        var exception = await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Contains("only by case", exception.Message);
        Assert.DoesNotContain("interactive login", exception.Message);
        AssertNothingChanged();
    }

    [Fact]
    public async Task ExactName_OverAnInteractiveLogin_Refuses()
    {
        var manager = StoreContext(interactiveLogin: true);
        manager.SelectContext(StoredName);
        var command = NewCommand(manager, new RecordingLogger<LogInClientCredentialsCommand>());
        command.CommandArgumentValue.ParseLayer(["-id", "ci"]);

        var exception = await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Contains($"Context '{StoredName}' holds an interactive login", exception.Message);
        Assert.DoesNotContain("only by case", exception.Message);
        AssertNothingChanged();
    }

    [Fact]
    public async Task ActiveContext_WithAnInteractiveLogin_Refuses()
    {
        // No --context: the persisted active context is the target, and its login is protected the same way.
        var manager = StoreContext(interactiveLogin: true);
        var command = NewCommand(manager, new RecordingLogger<LogInClientCredentialsCommand>());
        command.CommandArgumentValue.ParseLayer(["-id", "ci"]);

        var exception = await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Contains($"Context '{StoredName}' holds an interactive login", exception.Message);
        AssertNothingChanged();
    }

    [Theory]
    [InlineData("--replace-login")]
    [InlineData("-rl")]
    public async Task ReplaceLogin_ProceedsOverACaseOnlyMatchAndAnInteractiveLogin(string flag)
    {
        var manager = StoreContext(interactiveLogin: true);
        manager.SelectContext("local_octosystem");
        var logger = new RecordingLogger<LogInClientCredentialsCommand>();
        var command = NewCommand(manager, logger);
        command.CommandArgumentValue.ParseLayer(["-id", "ci", flag]);

        await command.Execute();

        AssertLoggedIn();
        Assert.Contains(logger.Entries, e => e.Message.Contains($"into context '{StoredName}'"));
    }

    [Fact]
    public async Task ExactName_OverAClientCredentialsLogin_RenewsWithoutTheFlag()
    {
        // The usual CI case: re-running the login on a client-credentials context stays as before.
        var manager = StoreContext(interactiveLogin: false);
        manager.SelectContext(StoredName);
        var command = NewCommand(manager, new RecordingLogger<LogInClientCredentialsCommand>());
        command.CommandArgumentValue.ParseLayer(["-id", "ci"]);

        await command.Execute();

        AssertLoggedIn();
    }

    [Fact]
    public async Task ActiveContext_WithAClientCredentialsLogin_RenewsWithoutTheFlag()
    {
        var manager = StoreContext(interactiveLogin: false);
        var command = NewCommand(manager, new RecordingLogger<LogInClientCredentialsCommand>());
        command.CommandArgumentValue.ParseLayer(["-id", "ci"]);

        await command.Execute();

        AssertLoggedIn();
    }
}
