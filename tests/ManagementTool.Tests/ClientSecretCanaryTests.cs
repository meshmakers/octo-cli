using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FakeItEasy;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.ApiSecrets;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;
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
///     Client secrets never reach a log line, and a generated secret is shown exactly once. The real commands, the real SDK
///     clients (<see cref="IdentityServicesClient" />, <see cref="TenantClient" /> with the generated method clients) and
///     the real <see cref="Runner" /> talk to a local listener that plays identity (client read) and asset repository
///     (GraphQL). A canary, generated per run, is searched in everything captured: every log line at Trace level through
///     the NLog layout of <c>nlog.config</c>, standard output and standard error, and every request URL.
///     <list type="bullet">
///         <item>
///             <c>AddClientCredentialsClient</c>: the canary is the client secret (from standard input); it appears only in
///             the GraphQL request body, also when the method refuses or the service echoes it in its error text.
///         </item>
///         <item>
///             <c>CreateApiSecretClient</c>: the canary is the secret the <c>AddSecret</c> method generates; it appears
///             exactly once on standard output and in no log line.
///         </item>
///     </list>
/// </summary>
[Collection("NLog configuration")]
public sealed class ClientSecretCanaryTests : IDisposable
{
    private const string ClientRtId = "65f0c0ffee0000000000abcf";

    // The layout of the console target in src/ManagementTool/nlog.config.
    private const string ConsoleLayout =
        "${message} ${onexception:Exception information\\:${exception:format=type,message,method,StackTrace:maxInnerExceptionLevel=5:innerFormat=type,message,method,StackTrace}}";

    private readonly string _canary = "canary-" + Guid.NewGuid().ToString("N");
    private readonly FakeService _service = new();

    public void Dispose() => _service.Dispose();

    public static TheoryData<string, int> ClientSecretScenarios => new()
    {
        { "success", 0 },
        { "CLIENT_ALREADY_EXISTS", -3 },
        { "METHOD_FORBIDDEN", -3 },
        { "METHOD_HANDLER_NOT_AVAILABLE", -3 },
        { "unavailable", -3 }
    };

    [Theory]
    [MemberData(nameof(ClientSecretScenarios))]
    public async Task ClientSecret_IsOnlyInTheGraphQlBody_NeverInLogsConsoleOrUrls(string answer, int expectedExitCode)
    {
        _service.GraphQlAnswer = Answer("createClient", answer, null);

        var (exitCode, output) = await RunAsync(loggerFactory =>
        {
            var command = new AddClientCredentialsClient(loggerFactory.CreateLogger<AddClientCredentialsClient>(),
                Options(), Identity(), Tenant(), A.Fake<IAuthenticationService>(),
                new SecretInput(new FakeSecretConsole { IsInputRedirected = true, StandardInput = _canary + "\n" }));
            command.CommandArgumentValue.ParseLayer(["-id", "svc", "-n", "Service", "--secret-stdin"]);
            return command.Execute;
        });

        Assert.Equal(expectedExitCode, exitCode);
        Assert.DoesNotContain(_canary, output, StringComparison.Ordinal);
        Assert.All(_service.Requests, r => Assert.DoesNotContain(_canary, r.PathAndQuery, StringComparison.Ordinal));
        var graphQl = Assert.Single(_service.Requests, r => r.PathAndQuery == "/tenants/acme/GraphQL");
        using var body = JsonDocument.Parse(graphQl.Body);
        Assert.Equal(_canary,
            body.RootElement.GetProperty("variables").GetProperty("input").GetProperty("clientSecret").GetString());
        Assert.Single(_service.Requests, r => r.Body.Contains(_canary, StringComparison.Ordinal));
        Assert.DoesNotContain(_service.Requests, r => r.Method == "POST" && r.PathAndQuery.EndsWith("/v1/clients"));
    }

    [Fact]
    public async Task GeneratedSecret_IsPrintedExactlyOnce_AndNeverLogged()
    {
        _service.GraphQlAnswer = Answer("addSecret", "success", _canary);
        var memory = new MemoryTarget("memory") { Layout = ConsoleLayout };

        var (exitCode, output) = await RunAsync(loggerFactory =>
        {
            var command = new CreateApiSecretClient(loggerFactory.CreateLogger<CreateApiSecretClient>(), Options(),
                new ConsoleService(), Identity(), Tenant(), A.Fake<IAuthenticationService>());
            command.CommandArgumentValue.ParseLayer(["-cid", "svc", "-d", "deploy key"]);
            return command.Execute;
        }, memory);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, Occurrences(output, _canary));
        Assert.DoesNotContain(memory.Logs, line => line.Contains(_canary, StringComparison.Ordinal));
        Assert.All(_service.Requests, r => Assert.DoesNotContain(_canary, r.PathAndQuery + r.Body, StringComparison.Ordinal));
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static IOptions<OctoToolOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new OctoToolOptions { TenantId = "acme" });

    private IdentityServicesClient Identity() =>
        new(new IdentityServiceClientOptions { EndpointUri = _service.BaseUrl, TenantId = "acme" },
            A.Fake<IIdentityServiceClientAccessToken>());

    private TenantClient Tenant()
    {
        var token = A.Fake<ITenantClientAccessToken>();
        A.CallTo(() => token.AccessToken).Returns(null);
        return new TenantClient(new TenantClientOptions { EndpointUri = _service.BaseUrl, TenantId = "acme" }, token);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(Func<ILoggerFactory, Func<Task>> create,
        MemoryTarget? memory = null)
    {
        memory ??= new MemoryTarget("memory") { Layout = ConsoleLayout };
        using var loggerFactory = CreateLoggerFactory(memory);
        var execute = create(loggerFactory);

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

    /// <summary>The GraphQL answer of the method gateway; a refusal echoes the canary in its message when one is given.</summary>
    private string Answer(string field, string answer, string? result) => answer switch
    {
        "success" => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["data"] = new
            {
                runtime = new
                {
                    systemIdentityClients = new Dictionary<string, object>
                    {
                        [field] = new { invocationId = "inv-1", result = result ?? ClientRtId }
                    }
                }
            }
        }),
        "unavailable" => JsonSerializer.Serialize(new
        {
            errors = new[]
            {
                new
                {
                    message = $"Cannot query field '{field}' on type 'SystemIdentityClientMutations'.",
                    extensions = new { code = "FIELDS_ON_CORRECT_TYPE", number = "5.3.1" }
                }
            }
        }),
        _ => JsonSerializer.Serialize(new
        {
            errors = new[]
            {
                new
                {
                    message = $"Refused: {answer} for {_canary}",
                    extensions = new
                    {
                        code = answer,
                        parameters = new Dictionary<string, string>
                        {
                            ["methodId"] = $"System.Identity/Client.{field}-1", ["value"] = _canary
                        }
                    }
                }
            },
            data = new Dictionary<string, object?>
            {
                ["runtime"] = new { systemIdentityClients = new Dictionary<string, object?> { [field] = null } }
            }
        })
    };

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

    /// <summary>Identity (<c>GET {tenant}/v1/clients/{id}</c>) and asset repository GraphQL on one local port.</summary>
    private sealed class FakeService : IDisposable
    {
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

                var response = path.StartsWith("/acme/v1/clients/", StringComparison.Ordinal)
                    ? JsonSerializer.Serialize(new { clientId = "svc", rtId = ClientRtId })
                    : GraphQlAnswer;
                var bytes = Encoding.UTF8.GetBytes(response);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }
    }
}
