using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Users;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#7899 — RevokeUserSessions and RevokeShadowUserSessions: the bulk command is a dry run unless --apply is given,
///     both ask for confirmation before changing anything (unless -y), and the counts reach the operator.
/// </summary>
public class RevokeSessionsCommandTests
{
    private readonly IIdentityServicesClient _client = A.Fake<IIdentityServicesClient>();
    private readonly IConfirmationService _confirmation = A.Fake<IConfirmationService>();

    public RevokeSessionsCommandTests()
    {
        A.CallTo(() => _client.RevokeShadowUserSessions(A<bool>._)).ReturnsLazily((bool dryRun) =>
            Task.FromResult(new ShadowUserSessionRevocationResultDto
            {
                DryRun = dryRun, Scope = "shadowUsers", TenantId = "customer", Users = 3, Tokens = 7,
                Authorizations = 1, Sessions = 2, UserNames = ["xt_a_b", "xt_a_c"]
            }));
        A.CallTo(() => _client.RevokeUserSessions(A<string>._)).ReturnsLazily((string name) =>
            Task.FromResult(new UserSessionRevocationResultDto
            {
                UserName = name, Subject = "u1", SecurityStampRotated = true, RevokedTokens = 4,
                RevokedAuthorizations = 1, RemovedSessions = 2
            }));
    }

    [Fact]
    public async Task RevokeShadowUserSessions_WithoutApply_IsADryRun_AndAsksNothing()
    {
        var logger = new RecordingLogger<RevokeShadowUserSessions>();
        var command = Shadow(logger);
        command.CommandArgumentValue.ParseLayer([]);

        await command.Execute();

        A.CallTo(() => _client.RevokeShadowUserSessions(true)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _client.RevokeShadowUserSessions(false)).MustNotHaveHappened();
        A.CallTo(() => _confirmation.Confirm(A<string>._)).MustNotHaveHappened();
        Assert.Contains(logger.Entries, e => e.Message.Contains("Dry run") && e.Message.Contains("3 shadow user(s)"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("xt_a_b"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("1 more"));
    }

    [Fact]
    public async Task RevokeShadowUserSessions_WithApply_AsksAndRevokes()
    {
        A.CallTo(() => _confirmation.Confirm(A<string>._)).Returns(true);
        var command = Shadow(new RecordingLogger<RevokeShadowUserSessions>());
        command.CommandArgumentValue.ParseLayer(["--apply"]);

        await command.Execute();

        A.CallTo(() => _confirmation.Confirm(A<string>.That.Contains("'customer'"))).MustHaveHappenedOnceExactly();
        A.CallTo(() => _client.RevokeShadowUserSessions(false)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task RevokeShadowUserSessions_WithApply_DeclinedConfirmation_ChangesNothing()
    {
        A.CallTo(() => _confirmation.Confirm(A<string>._)).Returns(false);
        var command = Shadow(new RecordingLogger<RevokeShadowUserSessions>());
        command.CommandArgumentValue.ParseLayer(["-a"]);

        await Assert.ThrowsAsync<ToolException>(() => command.Execute());

        A.CallTo(() => _client.RevokeShadowUserSessions(A<bool>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task RevokeShadowUserSessions_WithApplyAndYes_SkipsTheQuestion()
    {
        var command = Shadow(new RecordingLogger<RevokeShadowUserSessions>());
        command.CommandArgumentValue.ParseLayer(["-a", "-y"]);

        await command.Execute();

        A.CallTo(() => _confirmation.Confirm(A<string>._)).MustNotHaveHappened();
        A.CallTo(() => _client.RevokeShadowUserSessions(false)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task RevokeUserSessions_AsksThenRevokes_AndReportsTheCounts()
    {
        A.CallTo(() => _confirmation.Confirm(A<string>._)).Returns(true);
        var logger = new RecordingLogger<RevokeUserSessions>();
        var command = new RevokeUserSessions(logger, Options(), _client, A.Fake<IAuthenticationService>(),
            _confirmation);
        command.CommandArgumentValue.ParseLayer(["-un", "Alice"]);

        await command.Execute();

        A.CallTo(() => _client.RevokeUserSessions("Alice")).MustHaveHappenedOnceExactly();
        A.CallTo(() => _confirmation.Confirm(A<string>.That.Contains("tenant 'customer'"))).MustHaveHappenedOnceExactly();
        Assert.Contains(logger.Entries, e => e.Message.Contains("4 token(s)") && e.Message.Contains("2 browser session(s)"));
    }

    [Fact]
    public async Task RevokeUserSessions_DeclinedConfirmation_ChangesNothing()
    {
        A.CallTo(() => _confirmation.Confirm(A<string>._)).Returns(false);
        var command = new RevokeUserSessions(new RecordingLogger<RevokeUserSessions>(), Options(), _client,
            A.Fake<IAuthenticationService>(), _confirmation);
        command.CommandArgumentValue.ParseLayer(["-un", "alice"]);

        await Assert.ThrowsAsync<ToolException>(() => command.Execute());

        A.CallTo(() => _client.RevokeUserSessions(A<string>._)).MustNotHaveHappened();
    }

    private RevokeShadowUserSessions Shadow(ILogger<RevokeShadowUserSessions> logger) =>
        new(logger, Options(), _client, A.Fake<IAuthenticationService>(), _confirmation);

    private static IOptions<OctoToolOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new OctoToolOptions { TenantId = "customer" });
}
