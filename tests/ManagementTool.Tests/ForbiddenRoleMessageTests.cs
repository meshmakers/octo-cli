using System.Net;
using System.Text;
using FakeItEasy;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.ApiSecrets;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
/// AB#8094: identity answers a refused API secret write with <c>403 {"message": "...requires the role 'SecretManagement'..."}</c>
/// (AB#5861). octo-cli prints that message and exits with -3; a 403 without a message keeps the ProvisionCurrentUser hint.
/// The real <see cref="IdentityServicesClient" /> talks to a local HTTP listener, the real <see cref="Runner" /> prints.
/// </summary>
public sealed class ForbiddenRoleMessageTests : IDisposable
{
    private const string RoleMessage =
        "Access denied: the endpoint requires the role 'SecretManagement' (the caller holds: TenantManagement).";

    private readonly HttpListener _listener = new();
    private readonly string _baseUri;
    private string? _responseBody;

    public ForbiddenRoleMessageTests()
    {
        var started = false;
        var uri = "";
        for (var port = 43000 + Random.Shared.Next(0, 2000); !started; port++)
        {
            try
            {
                uri = $"http://127.0.0.1:{port}/";
                _listener.Prefixes.Clear();
                _listener.Prefixes.Add(uri);
                _listener.Start();
                started = true;
            }
            catch (HttpListenerException)
            {
                // port taken, try the next one
            }
        }

        _baseUri = uri;
        _ = Task.Run(Serve);
    }

    public void Dispose() => _listener.Close();

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

            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            if (_responseBody != null)
            {
                var bytes = Encoding.UTF8.GetBytes(_responseBody);
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
            }

            context.Response.Close();
        }
    }

    private async Task<(int ExitCode, List<LogEntry> Errors)> RunCreateApiSecretClient()
    {
        var client = new IdentityServicesClient(
            new IdentityServiceClientOptions { EndpointUri = _baseUri, TenantId = "octosystem" },
            A.Fake<IIdentityServiceClientAccessToken>());
        var command = new CreateApiSecretClient(new RecordingLogger<CreateApiSecretClient>(),
            Options.Create(new OctoToolOptions { TenantId = "octosystem" }), A.Fake<IConsoleService>(), client,
            A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(["-cid", "my-client"]);

        var parser = A.Fake<ICommandParser>();
        A.CallTo(() => parser.ParseAndValidateAsync(A<string?>._))
            .Returns(Task.Run(command.Execute, TestContext.Current.CancellationToken));
        var contexts = A.Fake<IContextManager>();
        A.CallTo(() => contexts.GetEffectiveContextName()).Returns("test");
        var logger = new RecordingLogger<Runner>();

        var exitCode = await new Runner(logger, parser, contexts, ContextSelection.None).DoActionAsync();
        return (exitCode, logger.Entries.Where(e => e.Level == LogLevel.Error).ToList());
    }

    [Fact]
    public async Task Forbidden_WithMessageBody_PrintsTheMessageOnly_AndExitsWithMinusThree()
    {
        _responseBody = "{\"message\":\"" + RoleMessage + "\"}";

        var (exitCode, errors) = await RunCreateApiSecretClient();

        Assert.Equal(-3, exitCode);
        var error = Assert.Single(errors);
        Assert.Equal(RoleMessage, error.Message);
        Assert.DoesNotContain("ProvisionCurrentUser", error.Message);
    }

    [Fact]
    public async Task Forbidden_WithoutBody_KeepsTheProvisionCurrentUserHint()
    {
        _responseBody = null;

        var (exitCode, errors) = await RunCreateApiSecretClient();

        Assert.Equal(-3, exitCode);
        Assert.Equal(2, errors.Count);
        Assert.Contains("ProvisionCurrentUser", errors[1].Message);
    }

    [Theory]
    [InlineData("{\"title\":\"Forbidden\",\"status\":403}")]
    [InlineData("{\"message\":\"\"}")]
    [InlineData("{\"message\":42}")]
    [InlineData("not json at all {")]
    public async Task Forbidden_WithBodyWithoutUsableMessage_KeepsTheProvisionCurrentUserHint(string body)
    {
        _responseBody = body;

        var (exitCode, errors) = await RunCreateApiSecretClient();

        Assert.Equal(-3, exitCode);
        Assert.Contains(errors, e => e.Message.Contains("ProvisionCurrentUser"));
    }

    [Fact]
    public void ApiSecretWriteCommands_NameTheSecretManagementRoleInTheirHelp()
    {
        var options = Options.Create(new OctoToolOptions { TenantId = "octosystem" });
        var identity = A.Fake<IIdentityServicesClient>();
        var auth = A.Fake<IAuthenticationService>();
        var documentations = new Command<OctoToolOptions>[]
        {
            new CreateApiSecretClient(NullLogger<CreateApiSecretClient>.Instance, options, A.Fake<IConsoleService>(), identity, auth),
            new UpdateApiSecretClient(NullLogger<UpdateApiSecretClient>.Instance, options, identity, auth),
            new DeleteApiSecretClient(NullLogger<DeleteApiSecretClient>.Instance, options, identity, auth, A.Fake<IConfirmationService>()),
            new CreateApiSecretApiResource(NullLogger<CreateApiSecretApiResource>.Instance, options,
                A.Fake<IConsoleService>(), identity, auth),
            new UpdateApiSecretApiResource(NullLogger<UpdateApiSecretApiResource>.Instance, options, identity, auth),
            new DeleteApiSecretApiResource(NullLogger<DeleteApiSecretApiResource>.Instance, options, identity, auth, A.Fake<IConfirmationService>()),
        };

        foreach (var command in documentations)
        {
            var notes = command.GetDocumentation()?.Notes;
            Assert.True(notes != null && notes.Any(n => n.Contains("SecretManagement")),
                $"{command.GetType().Name} must name the role SecretManagement in its notes.");
        }
    }
}
