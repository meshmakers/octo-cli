using System.Net;
using System.Text;
using FakeItEasy;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.CrossTenantGrantPermissions;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.ExternalTenantUserMappings;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#8346: sibling-tenant grants from octo-cli. The real commands and the real <see cref="IdentityServicesClient" />
///     talk to a local HTTP listener that answers like the identity service (AB#8341, AB#8343, AB#8345); the real
///     <see cref="Runner" /> turns failures into one readable line and exit code -3. The looked-up identifier never
///     appears in a log line.
/// </summary>
public sealed class CrossTenantGrantCommandTests : IDisposable
{
    private const string Identifier = "alice@meshmakers.io";

    private readonly HttpListener _listener = new();
    private readonly string _baseUri;
    private readonly List<(string Method, string Path, string Body, string? IfMatch)> _requests = [];
    private Func<string, string, (int Status, string Body)> _answer = (_, _) => (200, "{}");

    public CrossTenantGrantCommandTests()
    {
        var started = false;
        var uri = "";
        var firstPort = 49000 + Random.Shared.Next(0, 2000);
        for (var port = firstPort; !started; port++)
        {
            try
            {
                uri = $"http://127.0.0.1:{port}/";
                _listener.Prefixes.Clear();
                _listener.Prefixes.Add(uri);
                _listener.Start();
                started = true;
            }
            catch (HttpListenerException) when (port < firstPort + 100)
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

            var method = context.Request.HttpMethod;
            var path = context.Request.Url?.AbsolutePath ?? "";
            string body;
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync();
            }

            lock (_requests)
            {
                _requests.Add((method, path, body, context.Request.Headers["If-Match"]));
            }

            var (status, responseBody) = _answer(method, path);
            var bytes = Encoding.UTF8.GetBytes(responseBody);
            context.Response.StatusCode = status;
            context.Response.ContentType = status >= 400 ? "application/problem+json" : "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }

    private IdentityServicesClient Client(string tenantId) =>
        new(new IdentityServiceClientOptions { EndpointUri = _baseUri, TenantId = tenantId },
            A.Fake<IIdentityServiceClientAccessToken>());

    private static IOptions<OctoToolOptions> Options(string tenantId) =>
        Microsoft.Extensions.Options.Options.Create(new OctoToolOptions { TenantId = tenantId });

    private sealed record Run(int ExitCode, List<LogEntry> Logs, List<string> Console);

    private static async Task<Run> RunThroughRunner(Func<Task> execute, List<LogEntry> commandLogs,
        List<string> console)
    {
        var parser = A.Fake<ICommandParser>();
        A.CallTo(() => parser.ParseAndValidateAsync(A<string?>._))
            .Returns(Task.Run(execute, TestContext.Current.CancellationToken));
        var contexts = A.Fake<IContextManager>();
        A.CallTo(() => contexts.GetEffectiveContextName()).Returns("test");
        var runnerLogger = new RecordingLogger<Runner>();

        var exitCode = await new Runner(runnerLogger, parser, contexts, ContextSelection.None).DoActionAsync();
        return new Run(exitCode, commandLogs.Concat(runnerLogger.Entries).ToList(), console);
    }

    private static IConsoleService RecordingConsole(List<string> lines)
    {
        var console = A.Fake<IConsoleService>();
        A.CallTo(() => console.WriteLine(A<string>._)).Invokes((string line) => lines.Add(line));
        return console;
    }

    private async Task<Run> Lookup(string sourceTenantId = "meshmakers", string identifier = Identifier)
    {
        var logger = new RecordingLogger<LookupSourceTenantUser>();
        var lines = new List<string>();
        var command = new LookupSourceTenantUser(logger, Options("customer"), RecordingConsole(lines),
            Client("customer"), A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(["-st", sourceTenantId, "-id", identifier]);
        return await RunThroughRunner(command.Execute, logger.Entries, lines);
    }

    private async Task<Run> Grant(params string[] extra)
    {
        var logger = new RecordingLogger<CreateExternalTenantUserMapping>();
        var lines = new List<string>();
        var command = new CreateExternalTenantUserMapping(logger, Options("customer"), RecordingConsole(lines),
            Client("customer"), A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(
            ["-stid", "meshmakers", "-suid", "686a6215fd3e5972ff5693fd", "-sun", "alice", .. extra]);
        return await RunThroughRunner(command.Execute, logger.Entries, lines);
    }

    private async Task<Run> SetPermissions(params string[] args)
    {
        var logger = new RecordingLogger<SetCrossTenantGrantPermissions>();
        var lines = new List<string>();
        var command = new SetCrossTenantGrantPermissions(logger, Options("meshmakers"), RecordingConsole(lines),
            Client("meshmakers"), A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(args);
        return await RunThroughRunner(command.Execute, logger.Entries, lines);
    }

    private async Task<Run> GetPermissions()
    {
        var logger = new RecordingLogger<GetCrossTenantGrantPermissions>();
        var lines = new List<string>();
        var command = new GetCrossTenantGrantPermissions(logger, Options("meshmakers"), RecordingConsole(lines),
            Client("meshmakers"), A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer([]);
        return await RunThroughRunner(command.Execute, logger.Entries, lines);
    }

    private static void AssertIdentifierNotLogged(Run run) =>
        Assert.DoesNotContain(run.Logs, e => e.Message.Contains(Identifier, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task Lookup_Hit_PrintsTheUser_ExitZero_AndNeverLogsTheIdentifier()
    {
        _answer = (_, _) => (200, "{\"sourceTenantId\":\"meshmakers\",\"userId\":\"686a6215fd3e5972ff5693fd\"," +
                                  "\"userName\":\"alice\",\"displayName\":\"Alice A.\"}");

        var run = await Lookup();

        Assert.Equal(0, run.ExitCode);
        var request = Assert.Single(_requests);
        Assert.Equal("POST", request.Method);
        Assert.EndsWith("/customer/v1/externalTenantUserMappings/sourceUserLookup", request.Path);
        Assert.Contains("\"UserName\": \"alice\"", Assert.Single(run.Console));
        Assert.DoesNotContain(Identifier, run.Console[0]);
        AssertIdentifierNotLogged(run);
    }

    [Fact]
    public async Task Lookup_UniformMiss_PrintsTheNeutralMessage_AndExitsWithMinusThree()
    {
        _answer = (_, _) => (404, "{\"status\":404,\"title\":\"Source user not found\"," +
                                  "\"detail\":\"No user of the source tenant can be granted access with this identifier.\"," +
                                  "\"code\":\"SourceUserNotFound\"}");

        var run = await Lookup();

        Assert.Equal(-3, run.ExitCode);
        Assert.Empty(run.Console);
        Assert.Contains(run.Logs, e => e.Level == LogLevel.Error &&
                                       e.Message.Contains(LookupSourceTenantUser.NotFoundMessage));
        AssertIdentifierNotLogged(run);
    }

    [Fact]
    public async Task Lookup_RateLimited_NamesTheWaitTime_AndExitsWithMinusThree()
    {
        _answer = (_, _) => (429, "{\"status\":429,\"title\":\"Too many lookups\"," +
                                  "\"detail\":\"Too many source user lookups. Try again later.\"," +
                                  "\"code\":\"SourceUserLookupRateLimited\",\"retryAfterSeconds\":240}");

        var run = await Lookup();

        Assert.Equal(-3, run.ExitCode);
        var error = Assert.Single(run.Logs, e => e.Level == LogLevel.Error);
        Assert.Equal(
            "Too many lookups: Too many source user lookups. Try again later. (HTTP 429, code SourceUserLookupRateLimited). Retry after 240 s",
            error.Message);
        AssertIdentifierNotLogged(run);
    }

    [Fact]
    public async Task Grant_WithTwoGroups_ResolvesNames_PostsGroupIds_AndPrintsTheStoredMapping()
    {
        _answer = (method, path) => (method, path) switch
        {
            ("GET", var p) when p.EndsWith("/groups/names/Operators") =>
                (200, "{\"id\":\"6a1b2c3d4e5f60718293a4b5\",\"groupName\":\"Operators\"}"),
            ("POST", var p) when p.EndsWith("/externalTenantUserMappings") =>
                (201, "{\"id\":\"6a1b2c3d4e5f60718293a4c0\",\"sourceTenantId\":\"meshmakers\"," +
                      "\"sourceUserId\":\"686a6215fd3e5972ff5693fd\",\"sourceUserName\":\"alice\"," +
                      "\"roleIds\":[],\"groupNames\":[\"Operators\",\"Viewers\"]}"),
            _ => (500, "{}")
        };

        var run = await Grant("-g", "Operators, 6a1b2c3d4e5f60718293a4b6");

        Assert.Equal(0, run.ExitCode);
        var post = Assert.Single(_requests, r => r.Method == "POST");
        Assert.Contains("\"groupIds\":[\"6a1b2c3d4e5f60718293a4b5\",\"6a1b2c3d4e5f60718293a4b6\"]", post.Body);
        var output = Assert.Single(run.Console);
        Assert.Contains("\"SourceTenantId\": \"meshmakers\"", output);
        Assert.Contains("\"Operators\"", output);
        Assert.Contains("\"Viewers\"", output);
    }

    [Fact]
    public async Task Grant_UnknownGroupName_FailsBeforeTheMappingIsCreated()
    {
        _answer = (method, _) => method == "GET" ? (404, "") : (500, "{}");

        var run = await Grant("-g", "Nope");

        Assert.Equal(-5, run.ExitCode);
        Assert.DoesNotContain(_requests, r => r.Method == "POST");
        Assert.Contains(run.Logs, e => e.Level == LogLevel.Error && e.Message.Contains("Group 'Nope' does not exist"));
    }

    [Theory]
    [InlineData(403, "{\"status\":403,\"title\":\"Source tenant not permitted\",\"code\":\"SourceTenantNotPermitted\"}",
        "Source tenant not permitted (HTTP 403, code SourceTenantNotPermitted)")]
    [InlineData(400, "{\"status\":400,\"title\":\"Unknown group\",\"code\":\"UnknownGroup\",\"unknownGroupIds\":[\"6a1b2c3d4e5f60718293a4b6\"]}",
        "Unknown group (HTTP 400, code UnknownGroup). Unknown group ids: 6a1b2c3d4e5f60718293a4b6")]
    public async Task Grant_Refused_PrintsOneReadableLine_WithoutTheProvisionHint(int status, string body,
        string expected)
    {
        _answer = (_, _) => (status, body);

        var run = await Grant("-g", "6a1b2c3d4e5f60718293a4b6");

        Assert.Equal(-3, run.ExitCode);
        var error = Assert.Single(run.Logs, e => e.Level == LogLevel.Error);
        Assert.Equal(expected, error.Message);
        Assert.DoesNotContain("ProvisionCurrentUser", error.Message);
    }

    [Fact]
    public async Task GetPermissions_PrintsTheList()
    {
        _answer = (_, _) => (200, "{\"targetTenantIds\":[\"customer\"]}");

        var run = await GetPermissions();

        Assert.Equal(0, run.ExitCode);
        Assert.EndsWith("/meshmakers/v1/crossTenantGrantPermissions", Assert.Single(_requests).Path);
        Assert.Contains("\"customer\"", Assert.Single(run.Console));
    }

    [Fact]
    public async Task GetPermissions_ForeignAdministrator_Gets403_ExitMinusThree()
    {
        _answer = (_, _) => (403, "");

        var run = await GetPermissions();

        Assert.Equal(-3, run.ExitCode);
        Assert.Contains(run.Logs, e => e.Level == LogLevel.Error && e.Message.Contains("HTTP 403"));
    }

    [Fact]
    public async Task SetPermissions_ReadsThenReplaces_AndLogsAddedAndRemoved()
    {
        _answer = (method, _) => method == "GET"
            ? (200, "{\"targetTenantIds\":[\"old-customer\",\"customer\"]}")
            : (200, "{\"targetTenantIds\":[\"customer\",\"new-customer\"]}");

        var run = await SetPermissions("-ttids", "customer,New-Customer");

        Assert.Equal(0, run.ExitCode);
        var put = Assert.Single(_requests, r => r.Method == "PUT");
        Assert.Contains("\"targetTenantIds\":[\"customer\",\"New-Customer\"]", put.Body);
        Assert.Null(put.IfMatch);
        Assert.Contains(run.Logs, e => e.Message.Contains("added [new-customer], removed [old-customer]"));
    }

    [Fact]
    public async Task SetPermissions_None_SendsAnEmptyList()
    {
        _answer = (method, _) => method == "GET"
            ? (200, "{\"targetTenantIds\":[\"customer\"]}")
            : (200, "{\"targetTenantIds\":[]}");

        var run = await SetPermissions("--none");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("\"targetTenantIds\":[]", Assert.Single(_requests, r => r.Method == "PUT").Body);
    }

    [Theory]
    [InlineData]
    [InlineData("--none", "-ttids", "customer")]
    public async Task SetPermissions_NeedsExactlyOneOfListOrNone_NoCall(params string[] args)
    {
        var run = await SetPermissions(args);

        Assert.Equal(-5, run.ExitCode);
        Assert.Empty(_requests);
    }
}
