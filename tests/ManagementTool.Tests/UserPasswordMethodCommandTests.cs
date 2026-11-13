using System.Text.Json;
using FakeItEasy;
using GraphQL;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Users;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#8418 — <c>CreateUser</c> and <c>ResetPassword</c> run the CK methods <c>System.Identity/User.CreateUser-1</c> /
///     <c>User.ResetPassword-1</c> through the generated <c>RtUserMethodClient</c>: names, arguments and output are
///     unchanged, the REST methods are never called (no fallback, AB#7039), every declared error and every refusal of the
///     server ends the command with its code and message and exit code -3, and a server without the method gets a message
///     naming the needed identity version. The commands run through the real <see cref="Runner" />, so the exit code is
///     the one a user sees.
/// </summary>
public sealed class UserPasswordMethodCommandTests
{
    private const string UserRtId = "65f0c0ffee0000000000abcd";

    // Throwaway value, generated per test run; never a real password.
    private readonly string _password = "canary-" + Guid.NewGuid().ToString("N");

    private readonly IIdentityServicesClient _identity = A.Fake<IIdentityServicesClient>();
    private readonly ITenantClient _tenant = A.Fake<ITenantClient>();
    private readonly IAuthenticationService _auth = A.Fake<IAuthenticationService>();
    private readonly IConfirmationService _confirm = A.Fake<IConfirmationService>();
    private readonly List<GraphQLRequest> _requests = [];

    public UserPasswordMethodCommandTests()
    {
        A.CallTo(() => _confirm.Confirm(A<string>._)).Returns(true);
        A.CallTo(() => _identity.GetUserByNameEmailOrId(A<string>._)).ReturnsLazily((string name) =>
            Task.FromResult(new UserDto { Name = name, UserId = UserRtId }));
        A.CallTo(() => _tenant.ServiceUri).Returns(new Uri("https://assets.example/tenants/acme/GraphQL"));
        MethodFakes.AnswerMethodCalls(_tenant, _requests, null);
    }

    // ------------------------------------------------------------------ success, unchanged output

    [Fact]
    public async Task CreateUser_RunsTheCreateUserMethod_WithUnchangedOutput_AndNoRestCall()
    {
        var logger = new RecordingLogger<CreateUser>();

        await Create(logger, "-un", "Bob", "-e", "Bob@Example.com", "--password-stdin").Execute();

        var request = Assert.Single(_requests);
        Assert.Contains("createUser", request.Query, StringComparison.Ordinal);
        var input = Input(request);
        Assert.Equal("bob", input.GetProperty("userName").GetString());
        Assert.Equal("bob@example.com", input.GetProperty("email").GetString());
        Assert.Equal(_password, input.GetProperty("password").GetString());
        A.CallTo(() => _auth.EnsureAuthenticated(_tenant.AccessToken)).MustHaveHappened();
        AssertNoRestPasswordCall();
        Assert.Collection(logger.Entries,
            e => Assert.StartsWith("Creating user 'bob' at ", e.Message),
            e => Assert.Equal("User 'bob' added", e.Message));
    }

    [Fact]
    public async Task ResetPassword_RunsTheResetPasswordMethod_ByRtId_WithUnchangedOutput_AndNoRestCall()
    {
        var logger = new RecordingLogger<ResetPassword>();

        await Reset(logger, "-un", "Alice", "--password-stdin", "-y").Execute();

        var request = Assert.Single(_requests);
        Assert.Contains("resetPassword", request.Query, StringComparison.Ordinal);
        Assert.Equal(UserRtId, Variables(request).GetProperty("rtId").GetString());
        Assert.Equal(_password, Input(request).GetProperty("newPassword").GetString());
        A.CallTo(() => _identity.GetUserByNameEmailOrId("alice")).MustHaveHappenedOnceExactly();
        AssertNoRestPasswordCall();
        Assert.Collection(logger.Entries,
            e => Assert.StartsWith("Resetting password for user 'alice' at ", e.Message),
            e => Assert.Equal("Resetting password for user 'alice' done", e.Message));
    }

    // ------------------------------------------------------------------ declared errors (one test per error)

    [Theory]
    [InlineData("USER_NAME_RESERVED", "The user name is reserved for cross-tenant shadow users.")]
    [InlineData("USER_ALREADY_EXISTS", "A user with this name or e-mail address exists.")]
    [InlineData("PASSWORD_POLICY_VIOLATION", "Passwords must have at least one digit ('0'-'9').")]
    public async Task CreateUser_DeclaredError_PrintsCodeAndMessage_ExitsMinusThree(string code, string message)
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, code, message);
        var logger = new RecordingLogger<CreateUser>();

        var (exitCode, errors) = await RunAsync(Create(logger, "-un", "bob", "-e", "bob@example.com", "--password-stdin").Execute);

        Assert.Equal(-3, exitCode);
        Assert.Equal($"{code}: {message}", Assert.Single(errors).Message);
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("added"));
        AssertNoRestPasswordCall();
    }

    [Theory]
    [InlineData("PASSWORD_POLICY_VIOLATION", "Passwords must be at least 8 characters.")]
    [InlineData("SHADOW_USER_PASSWORD_NOT_ALLOWED", "A cross-tenant shadow user has no password.")]
    public async Task ResetPassword_DeclaredError_PrintsCodeAndMessage_ExitsMinusThree(string code, string message)
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, code, message);
        var logger = new RecordingLogger<ResetPassword>();

        var (exitCode, errors) = await RunAsync(Reset(logger, "-un", "alice", "--password-stdin", "-y").Execute);

        Assert.Equal(-3, exitCode);
        Assert.Equal($"{code}: {message}", Assert.Single(errors).Message);
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("done"));
        AssertNoRestPasswordCall();
    }

    // ------------------------------------------------------------------ refusals of the platform (abuse cases)

    [Theory]
    [InlineData("METHOD_FORBIDDEN", "role-required", "create")]
    [InlineData("METHOD_FORBIDDEN", "role-required", "reset")]
    [InlineData("METHOD_FORBIDDEN", "tenant-not-allowed", "create")]
    [InlineData("METHOD_FORBIDDEN", "tenant-not-allowed", "reset")]
    [InlineData("METHOD_TARGET_NOT_FOUND", "not-found", "reset")]
    public async Task Refusal_PrintsCodeAndReason_ExitsMinusThree_WithoutRestFallback(string code, string reason,
        string command)
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, code, "The CK method call was refused.",
            new Dictionary<string, object?> { ["reason"] = reason });

        var (exitCode, errors) = await RunAsync(command == "create"
            ? Create(new RecordingLogger<CreateUser>(), "-un", "bob", "-e", "bob@example.com", "--password-stdin").Execute
            : (Func<Task>)Reset(new RecordingLogger<ResetPassword>(), "-un", "alice", "--password-stdin", "-y").Execute);

        Assert.Equal(-3, exitCode);
        var error = Assert.Single(errors).Message;
        Assert.Contains($"failed: {code}", error, StringComparison.Ordinal);
        Assert.Contains($"reason={reason}", error, StringComparison.Ordinal);
        Assert.Single(_requests);
        AssertNoRestPasswordCall();
    }

    [Fact]
    public async Task ForeignTenantToken_HttpForbidden_ExitsMinusThree_WithoutRestFallback()
    {
        // A token of tenant A against tenant B: the asset repository answers 403 before GraphQL (TenantClient wraps it).
        A.CallTo(_tenant).Where(c => c.Method.Name == nameof(ITenantClient.SendMutationAsync))
            .WithNonVoidReturnType()
            .Throws(new ServiceClientException("Call to GraphQL source failed."));

        var (exitCode, errors) = await RunAsync(Reset(new RecordingLogger<ResetPassword>(), "-un", "alice",
            "--password-stdin", "-y").Execute);

        Assert.Equal(-3, exitCode);
        Assert.Equal("Call to GraphQL source failed.", Assert.Single(errors).Message);
        AssertNoRestPasswordCall();
    }

    [Fact]
    public async Task ResetPassword_UnknownUser_StopsBeforeTheMethodCall()
    {
        // A user of another tenant is not found in the tenant of the context.
        A.CallTo(() => _identity.GetUserByNameEmailOrId(A<string>._)).Returns(new UserDto { Name = "ghost" });

        var (exitCode, errors) = await RunAsync(Reset(new RecordingLogger<ResetPassword>(), "-un", "ghost",
            "--password-stdin", "-y").Execute);

        Assert.Equal(-3, exitCode);
        Assert.Equal("User with name 'ghost' not found.", Assert.Single(errors).Message);
        Assert.Empty(_requests);
        AssertNoRestPasswordCall();
    }

    // ------------------------------------------------------------------ server without the method

    [Theory]
    [InlineData("create", "validation")]
    [InlineData("create", "no-handler")]
    [InlineData("reset", "validation")]
    [InlineData("reset", "no-handler")]
    public async Task ServerWithoutTheMethod_NamesTheNeededIdentityVersion_AndMakesNoRestCall(string command,
        string answer)
    {
        var isCreate = command == "create";
        if (answer == "validation")
        {
            MethodFakes.AnswerMethodCallsWithValidationErrors(_tenant, _requests,
                isCreate ? "SystemIdentityUserCreateUserMethodInput" : "SystemIdentityUserResetPasswordMethodInput",
                isCreate ? "createUser" : "resetPassword");
        }
        else
        {
            MethodFakes.AnswerMethodCalls(_tenant, _requests, "METHOD_HANDLER_NOT_AVAILABLE", "No live handler.",
                new Dictionary<string, object?> { ["reason"] = "no-live-handler" });
        }

        var (exitCode, errors) = await RunAsync(isCreate
            ? Create(new RecordingLogger<CreateUser>(), "-un", "bob", "-e", "bob@example.com", "--password-stdin").Execute
            : (Func<Task>)Reset(new RecordingLogger<ResetPassword>(), "-un", "alice", "--password-stdin", "-y").Execute);

        Assert.Equal(-3, exitCode);
        var error = Assert.Single(errors).Message;
        var version = isCreate
            ? UserPasswordMethodCall.CreateUserMinimumIdentityVersion
            : UserPasswordMethodCall.ResetPasswordMinimumIdentityVersion;
        Assert.Contains($"needs identity services {version} or newer", error, StringComparison.Ordinal);
        Assert.Contains("Methods:Identity:EnablePasswordMethods", error, StringComparison.Ordinal);
        Assert.Contains(isCreate ? "The user was not created." : "The password was not changed.", error,
            StringComparison.Ordinal);
        Assert.Contains("does not fall back to the REST endpoint", error, StringComparison.Ordinal);
        Assert.Single(_requests);
        AssertNoRestPasswordCall();
    }

    // ------------------------------------------------------------------ the password never reaches output

    [Fact]
    public async Task ServerEchoingThePassword_InMessageAndParameters_IsRedacted()
    {
        // Defence in depth: even a server that echoes the value does not get it printed.
        MethodFakes.AnswerMethodCalls(_tenant, _requests, "PASSWORD_POLICY_VIOLATION",
            $"Password '{_password}' is too weak.", new Dictionary<string, object?> { ["value"] = _password });

        var (exitCode, errors) = await RunAsync(Reset(new RecordingLogger<ResetPassword>(), "-un", "alice",
            "--password-stdin", "-y").Execute);

        Assert.Equal(-3, exitCode);
        var error = Assert.Single(errors).Message;
        Assert.DoesNotContain(_password, error, StringComparison.Ordinal);
        Assert.Equal("PASSWORD_POLICY_VIOLATION: Password '[REDACTED]' is too weak. (value=[REDACTED])", error);
    }

    [Fact]
    public async Task UndeclaredRefusal_EchoingThePassword_IsRedacted()
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, "METHOD_VALIDATION_FAILED", "bad input",
            new Dictionary<string, object?> { ["newPassword"] = _password });

        var (exitCode, errors) = await RunAsync(Reset(new RecordingLogger<ResetPassword>(), "-un", "alice",
            "--password-stdin", "-y").Execute);

        Assert.Equal(-3, exitCode);
        Assert.DoesNotContain(_password, Assert.Single(errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Scrub_ReplacesEveryOccurrence_AndLeavesTextWithoutSecretUnchanged()
    {
        Assert.Equal("a [REDACTED] b [REDACTED]", UserPasswordMethodCall.Scrub("a pw1 b pw1", "pw1"));
        Assert.Equal("nothing here", UserPasswordMethodCall.Scrub("nothing here", null));
        Assert.Equal("nothing here", UserPasswordMethodCall.Scrub("nothing here", ""));
    }

    [Fact]
    public void CommandNotes_NameTheVersionsTheMessagesUse()
    {
        // The notes are written inline for the command-reference generator; they must match the constants.
        var createNotes = string.Join(' ', Create(new RecordingLogger<CreateUser>(), "-un", "x", "-e", "x@example.com")
            .GetDocumentation()!.Notes!);
        var resetNotes = string.Join(' ', Reset(new RecordingLogger<ResetPassword>(), "-un", "x").GetDocumentation()!.Notes!);

        Assert.Contains($"identity services {UserPasswordMethodCall.CreateUserMinimumIdentityVersion} or newer", createNotes);
        Assert.Contains($"identity services {UserPasswordMethodCall.ResetPasswordMinimumIdentityVersion} or newer", resetNotes);
    }

    // ------------------------------------------------------------------ helpers

    private CreateUser Create(RecordingLogger<CreateUser> logger, params string[] args)
    {
        var command = new CreateUser(logger, Options(), _identity, _tenant, _auth, StdinSecret());
        command.CommandArgumentValue.ParseLayer(args);
        return command;
    }

    private ResetPassword Reset(RecordingLogger<ResetPassword> logger, params string[] args)
    {
        var command = new ResetPassword(logger, Options(), _identity, _tenant, _auth, _confirm, StdinSecret());
        command.CommandArgumentValue.ParseLayer(args);
        return command;
    }

    private SecretInput StdinSecret() =>
        new(new FakeSecretConsole { IsInputRedirected = true, StandardInput = _password + "\n" });

    private static IOptions<OctoToolOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new OctoToolOptions { TenantId = "acme" });

    private static async Task<(int ExitCode, List<LogEntry> Errors)> RunAsync(Func<Task> execute)
    {
        var parser = A.Fake<ICommandParser>();
        A.CallTo(() => parser.ParseAndValidateAsync(A<string?>._)).ReturnsLazily(execute);
        var contexts = A.Fake<IContextManager>();
        A.CallTo(() => contexts.GetEffectiveContextName()).Returns("test");
        var logger = new RecordingLogger<Runner>();

        var exitCode = await new Runner(logger, parser, contexts, ContextSelection.None).DoActionAsync();
        return (exitCode, logger.Entries.Where(e => e.Level == LogLevel.Error).ToList());
    }

    private void AssertNoRestPasswordCall() =>
        A.CallTo(_identity)
            .Where(c => c.Method.Name == nameof(IIdentityServicesClient.CreateUser) ||
                        c.Method.Name == nameof(IIdentityServicesClient.ResetPassword))
            .MustNotHaveHappened();

    private static JsonElement Variables(GraphQLRequest request) => JsonSerializer.SerializeToElement(request.Variables);

    private static JsonElement Input(GraphQLRequest request) => Variables(request).GetProperty("input");
}
