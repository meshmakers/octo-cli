using System.Net;
using System.Text;
using FakeItEasy;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Users;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#8839 — an identity service before 3.5.55 binds <c>userName</c> and <c>password</c> of <c>POST users/ResetPassword</c>
///     only from the query string and answers the body form (AB#8008) with a 400 validation problem naming both as missing.
///     The real command, the real <see cref="IdentityServicesClient" /> and the real <see cref="Runner" /> run against a local
///     listener that answers like the FH lab installation (OctoMesh 3.5.23) did: the command exits with -3 and one message
///     naming the needed version, the raw validation problem is not printed, and the password goes in no URL and no retry.
/// </summary>
public sealed class ResetPasswordOlderIdentityTests : IDisposable
{
    // The answer of identity 3.5.23 to the body form, recorded 2026-11-11.
    private const string OlderIdentityAnswer =
        "{\"type\":\"https://tools.ietf.org/html/rfc9110#section-15.5.1\",\"title\":\"One or more validation errors occurred.\"," +
        "\"status\":400,\"errors\":{\"password\":[\"The password field is required.\"],\"userName\":[\"The userName field is required.\"]}}";

    private readonly string _password = "pw-" + Guid.NewGuid().ToString("N");
    private readonly HttpListener _listener;
    private readonly string _baseUri;
    private readonly List<string> _requestUrls = [];
    private string _body = OlderIdentityAnswer;

    public ResetPasswordOlderIdentityTests()
    {
        (_listener, _baseUri) = StartListener();
        _ = Task.Run(Serve);
    }

    public void Dispose() => _listener.Close();

    [Fact]
    public async Task OlderIdentity_ExitsWithMinusThree_AndNamesTheNeededVersion_WithoutTheRawProblem()
    {
        var (exitCode, errors) = await RunResetPassword();

        Assert.Equal(-3, exitCode);
        var error = Assert.Single(errors);
        Assert.Contains("older than 3.5.55", error.Message, StringComparison.Ordinal);
        Assert.Contains("The password was not changed", error.Message, StringComparison.Ordinal);
        Assert.Contains("Forgot password", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("field is required", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_password, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OlderIdentity_NoFallbackToTheUrl_ExactlyOneRequest()
    {
        await RunResetPassword();

        var url = Assert.Single(_requestUrls);
        Assert.Equal("/acme/v1/users/ResetPassword", url);
        Assert.DoesNotContain(_password, url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OtherValidationProblem_IsReportedUnchanged()
    {
        // e.g. a password policy refusal of a current identity service: not the older-service answer.
        _body = "{\"title\":\"One or more validation errors occurred.\",\"status\":400,\"errors\":{\"Password\":[\"Passwords must have at least one digit.\"]}}";

        var (exitCode, errors) = await RunResetPassword();

        Assert.Equal(-3, exitCode);
        var error = Assert.Single(errors);
        Assert.Contains("at least one digit", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("3.5.55", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, OlderIdentityAnswer, true)]
    [InlineData(HttpStatusCode.BadRequest, "{\"errors\":{\"userName\":[\"x\"]}}", false)]
    [InlineData(HttpStatusCode.BadRequest, "User 'alice' not found.", false)]
    [InlineData(HttpStatusCode.NotFound, OlderIdentityAnswer, false)]
    public void Detection_NeedsA400NamingBothFields(HttpStatusCode status, string body, bool expected)
    {
        var exception = new ServiceClientResultException(body, status) { ResponseContent = body };

        Assert.Equal(expected, ResetPassword.IsBodyFormRefusedByOlderIdentity(exception, "alice", "pw-1"));
    }

    [Theory]
    [InlineData(" ", "pw-1")]
    [InlineData("alice", "   ")]
    [InlineData("\t", " ")]
    public void Detection_BlankValueSent_KeepsTheOriginalValidationErrors(string userName, string password)
    {
        // A current identity reports blank values as missing too; that answer must not be read as the older service.
        var exception = new ServiceClientResultException(OlderIdentityAnswer, HttpStatusCode.BadRequest)
            { ResponseContent = OlderIdentityAnswer };

        Assert.False(ResetPassword.IsBodyFormRefusedByOlderIdentity(exception, userName, password));
    }

    [Fact]
    public async Task BlankPassword_RefusedByTheService_IsReportedUnchanged()
    {
        var (exitCode, errors) = await RunResetPassword("   ");

        Assert.Equal(-3, exitCode);
        var error = Assert.Single(errors);
        Assert.Contains("field is required", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("3.5.55", error.Message, StringComparison.Ordinal);
    }

    private async Task<(int ExitCode, List<LogEntry> Errors)> RunResetPassword(string? password = null)
    {
        var client = new IdentityServicesClient(
            new IdentityServiceClientOptions { EndpointUri = _baseUri, TenantId = "acme" },
            A.Fake<IIdentityServiceClientAccessToken>());
        var command = new ResetPassword(new RecordingLogger<ResetPassword>(),
            Options.Create(new OctoToolOptions { TenantId = "acme" }), client, A.Fake<IAuthenticationService>(),
            A.Fake<IConfirmationService>(), FakeSecretConsole.Empty());
        command.CommandArgumentValue.ParseLayer(["-un", "alice", "-p", password ?? _password, "-y"]);

        var parser = A.Fake<ICommandParser>();
        A.CallTo(() => parser.ParseAndValidateAsync(A<string?>._))
            .Returns(Task.Run(command.Execute, TestContext.Current.CancellationToken));
        var contexts = A.Fake<IContextManager>();
        A.CallTo(() => contexts.GetEffectiveContextName()).Returns("test");
        var logger = new RecordingLogger<Runner>();

        var exitCode = await new Runner(logger, parser, contexts, ContextSelection.None).DoActionAsync();
        return (exitCode, logger.Entries.Where(e => e.Level == LogLevel.Error).ToList());
    }

    private static (HttpListener Listener, string BaseUri) StartListener()
    {
        const int attempts = 20;
        for (var attempt = 1; ; attempt++)
        {
            var uri = $"http://127.0.0.1:{49000 + Random.Shared.Next(0, 2000)}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(uri);
            try
            {
                listener.Start();
                return (listener, uri);
            }
            catch (HttpListenerException) when (attempt < attempts)
            {
                // port taken: try another one with a new listener
                listener.Close();
            }
        }
    }

    private async Task Serve()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception)
            {
                return;
            }

            lock (_requestUrls)
            {
                _requestUrls.Add(context.Request.Url?.PathAndQuery ?? string.Empty);
            }

            var bytes = Encoding.UTF8.GetBytes(_body);
            context.Response.StatusCode = 400;
            context.Response.ContentType = "application/problem+json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }
}
