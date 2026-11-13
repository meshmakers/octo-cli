using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FakeItEasy;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Users;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;
using NLogLevel = NLog.LogLevel;

namespace ManagementTool.Tests;

/// <summary>
///     AB#8418 — the password of <c>CreateUser</c> / <c>ResetPassword</c> never reaches output. The real commands, the real
///     SDK clients (<see cref="IdentityServicesClient" />, <see cref="TenantClient" /> with the generated
///     <c>RtUserMethodClient</c>) and the real <see cref="Runner" /> talk to a local listener that plays identity (user
///     lookup) and asset repository (GraphQL). A canary password, generated per run, is searched in everything captured:
///     every log line at Trace level through the NLog layout of <c>nlog.config</c>, standard output and standard error,
///     and every request URL. It must appear exactly once, in the GraphQL request body - the only place it belongs.
/// </summary>
[Collection("NLog configuration")]
public sealed class UserPasswordCanaryTests : IDisposable
{
    private const string UserRtId = "65f0c0ffee0000000000abcd";

    // The layout of the console target in src/ManagementTool/nlog.config.
    private const string ConsoleLayout =
        "${message} ${onexception:Exception information\\:${exception:format=type,message,method,StackTrace:maxInnerExceptionLevel=5:innerFormat=type,message,method,StackTrace}}";

    private readonly string _canary = "canary-" + Guid.NewGuid().ToString("N");
    private readonly FakeService _service = new();

    public void Dispose() => _service.Dispose();

    public static TheoryData<string, string, int> Scenarios => new()
    {
        { "create", "success", 0 },
        { "create", "USER_ALREADY_EXISTS", -3 },
        { "create", "PASSWORD_POLICY_VIOLATION", -3 },
        { "create", "METHOD_FORBIDDEN", -3 },
        { "create", "unavailable", -3 },
        { "reset", "success", 0 },
        { "reset", "PASSWORD_POLICY_VIOLATION", -3 },
        { "reset", "SHADOW_USER_PASSWORD_NOT_ALLOWED", -3 },
        { "reset", "METHOD_FORBIDDEN", -3 },
        { "reset", "METHOD_TARGET_NOT_FOUND", -3 },
        { "reset", "unavailable", -3 },
        { "reset", "http-403", -3 }
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Canary_IsOnlyInTheGraphQlBody_NeverInLogsConsoleOrUrls(string command, string answer,
        int expectedExitCode)
    {
        _service.GraphQlAnswer = Answer(command, answer);

        var (exitCode, output) = await RunAsync(command, ["--password-stdin"]);

        Assert.Equal(expectedExitCode, exitCode);
        Assert.DoesNotContain(_canary, output, StringComparison.Ordinal);
        Assert.All(_service.Requests, r => Assert.DoesNotContain(_canary, r.PathAndQuery, StringComparison.Ordinal));

        var graphQl = Assert.Single(_service.Requests, r => r.PathAndQuery == "/tenants/acme/GraphQL");
        Assert.Equal("POST", graphQl.Method);
        using var body = JsonDocument.Parse(graphQl.Body);
        var input = body.RootElement.GetProperty("variables").GetProperty("input");
        Assert.Equal(_canary, input.GetProperty(command == "create" ? "password" : "newPassword").GetString());
        Assert.Single(_service.Requests, r => r.Body.Contains(_canary, StringComparison.Ordinal));
        Assert.DoesNotContain(_service.Requests, r => r.PathAndQuery.Contains("ResetPassword", StringComparison.Ordinal));
        Assert.DoesNotContain(_service.Requests, r => r.Method == "POST" && r.PathAndQuery.EndsWith("/v1/users"));

        // The run is meaningful: the trace output is there and names the user.
        Assert.Contains(command == "create" ? "Creating user 'bob'" : "Resetting password for user 'alice'", output);
    }

    [Fact]
    public async Task DeprecatedArgument_WarnsWithoutTheValue_AndKeepsItOutOfLogsAndUrls()
    {
        _service.GraphQlAnswer = Answer("reset", "success");

        var (exitCode, output) = await RunAsync("reset", ["-p", _canary]);

        Assert.Equal(0, exitCode);
        Assert.Contains("deprecated", output, StringComparison.Ordinal);
        Assert.DoesNotContain(_canary, output, StringComparison.Ordinal);
        Assert.All(_service.Requests, r => Assert.DoesNotContain(_canary, r.PathAndQuery, StringComparison.Ordinal));
    }

    private async Task<(int ExitCode, string Output)> RunAsync(string commandName, string[] passwordArgs)
    {
        var memory = new MemoryTarget("memory") { Layout = ConsoleLayout };
        using var loggerFactory = CreateLoggerFactory(memory);

        var identity = new IdentityServicesClient(
            new IdentityServiceClientOptions { EndpointUri = _service.BaseUrl, TenantId = "acme" },
            A.Fake<IIdentityServiceClientAccessToken>());
        var token = A.Fake<ITenantClientAccessToken>();
        A.CallTo(() => token.AccessToken).Returns(null);
        var tenant = new TenantClient(new TenantClientOptions { EndpointUri = _service.BaseUrl, TenantId = "acme" }, token);
        var options = Options.Create(new OctoToolOptions { TenantId = "acme" });
        var secretInput = new SecretInput(new FakeSecretConsole
            { IsInputRedirected = true, StandardInput = _canary + "\n" });

        Func<Task> execute;
        if (commandName == "create")
        {
            var command = new CreateUser(loggerFactory.CreateLogger<CreateUser>(), options, identity, tenant,
                A.Fake<IAuthenticationService>(), secretInput);
            command.CommandArgumentValue.ParseLayer(["-un", "bob", "-e", "bob@example.com", .. passwordArgs]);
            execute = command.Execute;
        }
        else
        {
            var command = new ResetPassword(loggerFactory.CreateLogger<ResetPassword>(), options, identity, tenant,
                A.Fake<IAuthenticationService>(), A.Fake<IConfirmationService>(), secretInput);
            command.CommandArgumentValue.ParseLayer(["-un", "alice", "-y", .. passwordArgs]);
            execute = command.Execute;
        }

        var parser = A.Fake<ICommandParser>();
        A.CallTo(() => parser.ParseAndValidateAsync(A<string?>._)).ReturnsLazily(execute);
        var contexts = A.Fake<IContextManager>();
        A.CallTo(() => contexts.GetEffectiveContextName()).Returns("test");

        var previousOut = Console.Out;
        var previousError = Console.Error;
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exitCode;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            exitCode = await new Runner(loggerFactory.CreateLogger<Runner>(), parser, contexts, ContextSelection.None)
                .DoActionAsync();
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }

        return (exitCode, string.Join('\n', memory.Logs) + "\n" + stdout + "\n" + stderr);
    }

    private static string Answer(string command, string answer)
    {
        var field = command == "create" ? "createUser" : "resetPassword";
        return answer switch
        {
            "success" => JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["data"] = new { runtime = new { systemIdentityUsers = new Dictionary<string, object>
                {
                    [field] = command == "create"
                        ? new { invocationId = "inv-1", result = (string?)UserRtId }
                        : new { invocationId = "inv-1", result = (string?)null }
                } } }
            }),
            "unavailable" => JsonSerializer.Serialize(new
            {
                errors = new[]
                {
                    new
                    {
                        message = $"Cannot query field '{field}' on type 'SystemIdentityUserMutations'.",
                        extensions = new { code = "FIELDS_ON_CORRECT_TYPE", number = "5.3.1" }
                    }
                }
            }),
            "http-403" => FakeService.Forbidden,
            _ => JsonSerializer.Serialize(new
            {
                errors = new[]
                {
                    new
                    {
                        message = $"Refused: {answer}",
                        extensions = new
                        {
                            code = answer,
                            parameters = new Dictionary<string, string> { ["methodId"] = $"System.Identity/User.{field}-1" }
                        }
                    }
                },
                data = new Dictionary<string, object?>
                {
                    ["runtime"] = new { systemIdentityUsers = new Dictionary<string, object?> { [field] = null } }
                }
            })
        };
    }

    private static ILoggerFactory CreateLoggerFactory(Target target)
    {
        var configuration = new LoggingConfiguration();
        configuration.AddRule(NLogLevel.Trace, NLogLevel.Fatal, target);
        RedactedLogOutput.Apply(configuration);
        var logFactory = new NLog.LogFactory { Configuration = configuration };
        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(new NLogLoggerProvider(new NLogProviderOptions(), logFactory));
        });
    }

    private sealed record RecordedRequest(string Method, string PathAndQuery, string Body);

    /// <summary>Identity (<c>GET {tenant}/v1/users/{name}</c>) and asset repository GraphQL on one local port.</summary>
    private sealed class FakeService : IDisposable
    {
        public const string Forbidden = "<forbidden>";

        private readonly HttpListener _listener = new();
        private readonly List<RecordedRequest> _requests = [];
        private readonly Lock _sync = new();

        public FakeService()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            BaseUrl = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();
            _ = Task.Run(ServeAsync);
        }

        public string BaseUrl { get; }

        public string GraphQlAnswer { get; set; } = "{}";

        public IReadOnlyList<RecordedRequest> Requests
        {
            get
            {
                lock (_sync)
                {
                    return _requests.ToArray();
                }
            }
        }

        public void Dispose() => _listener.Abort();

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception e) when (e is HttpListenerException or ObjectDisposedException)
                {
                    return;
                }

                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                var path = context.Request.Url?.PathAndQuery ?? string.Empty;
                lock (_sync)
                {
                    _requests.Add(new RecordedRequest(context.Request.HttpMethod, path, body));
                }

                var status = 200;
                string response;
                if (path.StartsWith("/acme/v1/users/", StringComparison.Ordinal))
                {
                    response = JsonSerializer.Serialize(new { userId = UserRtId, name = "alice" });
                }
                else if (GraphQlAnswer == Forbidden)
                {
                    status = 403;
                    response = string.Empty;
                }
                else
                {
                    response = GraphQlAnswer;
                }

                var bytes = Encoding.UTF8.GetBytes(response);
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }
    }
}
