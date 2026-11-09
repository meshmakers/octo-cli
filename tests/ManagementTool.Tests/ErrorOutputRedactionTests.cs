using System.Net;
using System.Text;
using FakeItEasy;
using Meshmakers.Common.CommandLineParser;
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
using NLogLogManager = NLog.LogManager;

namespace ManagementTool.Tests;

/// <summary>
///     AB#8570: octo-cli output never carries the request's credentials. A service with the ASP.NET Core developer
///     exception page (local stack) answers a 500 with a plain-text page that echoes the request headers; the SDK puts
///     that body into the exception message, and <see cref="Runner" /> logs it. Every log line passes through
///     <see cref="ErrorOutputRedactor" /> in the NLog layout (<see cref="RedactedLogOutput" />), at every level.
///     Only obvious fake values are used.
/// </summary>
[Collection("NLog configuration")]
public sealed class ErrorOutputRedactionTests : IDisposable
{
    private const string Token = "test-token-redact-me";
    private const string Password = "test-password-redact-me";
    private const string ServerMessage = "System.InvalidOperationException: User store is not available.";

    // The layout of the console target in src/ManagementTool/nlog.config.
    private const string ConsoleLayout =
        "${message} ${onexception:Exception information\\:${exception:format=type,message,method,StackTrace:maxInnerExceptionLevel=5:innerFormat=type,message,method,StackTrace}}";

    private readonly HttpListener _listener;
    private readonly string _baseUri;
    private readonly List<(string Method, string Path)> _requests = [];

    public ErrorOutputRedactionTests()
    {
        (_listener, _baseUri) = StartListener();
        _ = Task.Run(Serve);
    }

    public void Dispose() => _listener.Close();

    // ---------------------------------------------------------------------------------------------------------------
    // The redactor
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Redact_DeveloperExceptionPage_DropsTheHeaderSection_KeepsTheServerMessage()
    {
        var page = DeveloperExceptionPage("Bearer " + Token, "{\"userName\":\"alice\",\"password\":\"" + Password + "\"}");

        var redacted = ErrorOutputRedactor.Redact("InternalServerError: " + page);

        Assert.DoesNotContain(Token, redacted);
        Assert.DoesNotContain(Password, redacted);
        Assert.DoesNotContain("session=", redacted);
        Assert.Contains("InternalServerError: " + ServerMessage, redacted);
        Assert.Contains("at Identity.Users.ResetPassword", redacted);
        Assert.Contains(ErrorOutputRedactor.HeaderSectionPlaceholder, redacted);
    }

    [Theory]
    [InlineData("Authorization: Bearer " + Token)]
    [InlineData("authorization: bearer " + Token)]
    [InlineData("Proxy-Authorization: Basic dGVzdC10b2tlbi1yZWRhY3QtbWU=")]
    [InlineData("Cookie: .AspNetCore.Identity=" + Token + "; other=1")]
    [InlineData("Set-Cookie: session=" + Token + "; path=/")]
    [InlineData("X-Api-Key: " + Token)]
    [InlineData("{\"Authorization\":\"Bearer " + Token + "\",\"status\":500}")]
    [InlineData("request failed, sent with bearer " + Token)]
    [InlineData("token eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiJ0ZXN0In0.c2lnbmF0dXJlLXJlZGFjdC1tZQ was refused")]
    [InlineData("{\"clientId\":\"cli\",\"clientSecret\":\"" + Token + "\"}")]
    [InlineData("{\"access_token\": \"" + Token + "\", \"expires_in\": 3600}")]
    [InlineData("{\"password\":\"with \\\"quote\\\" " + Token + "\"}")]
    [InlineData("grant_type=client_credentials&client_id=cli&client_secret=" + Token + "&scope=api")]
    [InlineData("refresh_token=" + Token)]
    public void Redact_RemovesCredentials(string text)
    {
        var redacted = ErrorOutputRedactor.Redact(text);

        Assert.DoesNotContain(Token, redacted);
        Assert.DoesNotContain("dGVzdC10b2tlbi1yZWRhY3QtbWU", redacted);
        Assert.DoesNotContain("c2lnbmF0dXJlLXJlZGFjdC1tZQ", redacted);
        Assert.Contains(ErrorOutputRedactor.Placeholder, redacted);
    }

    [Fact]
    public void Redact_KeepsNamesAndHarmlessFields()
    {
        var redacted = ErrorOutputRedactor.Redact(
            "grant_type=client_credentials&client_id=cli&client_secret=" + Token + "&scope=api");

        Assert.Equal("grant_type=client_credentials&client_id=cli&client_secret=[REDACTED]&scope=api", redacted);
        Assert.Equal("{\"clientId\":\"cli\",\"clientSecret\":\"[REDACTED]\"}",
            ErrorOutputRedactor.Redact("{\"clientId\":\"cli\",\"clientSecret\":\"" + Token + "\"}"));
        Assert.Equal("Authorization: [REDACTED]", ErrorOutputRedactor.Redact("Authorization: Bearer " + Token));
    }

    [Theory]
    [InlineData("Authorization failed: The access token is expired.")]
    [InlineData("POST http://localhost:5003/acme/v1/users/ResetPassword failed: InternalServerError")]
    [InlineData("Forbidden: {\"status\":403,\"title\":\"Missing role\",\"code\":\"RoleMissing\",\"role\":\"SecretManagement\"}")]
    [InlineData("Access denied (HTTP 403). If the context addresses a tenant other than the one you signed in on")]
    [InlineData("Token refresh failed (invalid_grant); falling back to device log-in.")]
    [InlineData("{\"secretIsSet\":true,\"description\":\"rotated\"}")]
    [InlineData("")]
    public void Redact_LeavesTextWithoutCredentialsUnchanged(string text) =>
        Assert.Same(text, ErrorOutputRedactor.Redact(text));

    // ---------------------------------------------------------------------------------------------------------------
    // The whole path: real command, real SDK client, real Runner, NLog layout of nlog.config
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task FailedServerCall_OutputHasNeitherTheAuthorizationHeaderNorTheBodySecret_ButStatusAndMessage()
    {
        var (exitCode, output) = await RunResetPasswordAgainstFailingServer(NLogLevel.Info);

        Assert.Equal(-3, exitCode);
        var request = Assert.Single(_requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/acme/v1/users/ResetPassword", request.Path);

        Assert.DoesNotContain(Token, output);
        Assert.DoesNotContain(Password, output);
        Assert.DoesNotContain("session=", output);
        Assert.Contains("InternalServerError", output);
        Assert.Contains(ServerMessage, output);
    }

    [Fact]
    public async Task FailedServerCall_AtTraceLevel_StillHasNoCredentials()
    {
        var (exitCode, output) = await RunResetPasswordAgainstFailingServer(NLogLevel.Trace,
            logExtra: logger => logger.LogTrace("Request headers: Authorization: Bearer {Token}", Token));

        Assert.Equal(-3, exitCode);
        Assert.DoesNotContain(Token, output);
        Assert.DoesNotContain(Password, output);
        Assert.Contains("Request headers: Authorization: [REDACTED]", output);
        Assert.Contains(ServerMessage, output);
    }

    [Fact]
    public void UnexpectedException_ExceptionLayoutIsRedacted()
    {
        var memory = new MemoryTarget("memory") { Layout = ConsoleLayout };
        using var loggerFactory = CreateLoggerFactory(memory, NLogLevel.Info);
        var logger = loggerFactory.CreateLogger("test");

        logger.LogCritical(new HttpRequestException("Cookie: session=" + Token), "{Message}", "unexpected");

        var output = string.Join('\n', memory.Logs);
        Assert.DoesNotContain(Token, output);
        Assert.Contains("unexpected", output);
        Assert.Contains("HttpRequestException", output);
    }

    [Fact]
    public void Enable_WrapsTheActiveAndEveryLaterConfiguration()
    {
        var previous = NLogLogManager.Configuration;
        try
        {
            var first = new MemoryTarget("first") { Layout = "${message}" };
            NLogLogManager.Configuration = Configuration(first, NLogLevel.Info);

            RedactedLogOutput.Enable();
            RedactedLogOutput.Enable();
            Assert.IsType<RedactedLogOutput.RedactingLayout>(first.Layout);
            Assert.IsNotType<RedactedLogOutput.RedactingLayout>(((RedactedLogOutput.RedactingLayout)first.Layout).Inner);

            // A reloaded / replaced configuration (e.g. a custom nlog.config at Trace level) is wrapped as well.
            var later = new MemoryTarget("later") { Layout = "${message}" };
            NLogLogManager.Configuration = Configuration(later, NLogLevel.Trace);
            NLogLogManager.GetLogger("test").Trace("Authorization: Bearer {0}", Token);

            Assert.IsType<RedactedLogOutput.RedactingLayout>(later.Layout);
            Assert.Equal("Authorization: [REDACTED]", Assert.Single(later.Logs));
        }
        finally
        {
            NLogLogManager.Configuration = previous;
        }
    }

    [Fact]
    public void StandardStreams_WriteError_IsRedacted()
    {
        var previous = Console.Error;
        var error = new StringWriter();
        try
        {
            Console.SetError(error);
            new StandardStreams().WriteError("{\"errors\":[{\"message\":\"denied\"}],\"access_token\":\"" + Token + "\"}");
        }
        finally
        {
            Console.SetError(previous);
        }

        Assert.DoesNotContain(Token, error.ToString());
        Assert.Contains("denied", error.ToString());
    }

    // ---------------------------------------------------------------------------------------------------------------

    private async Task<(int ExitCode, string Output)> RunResetPasswordAgainstFailingServer(NLogLevel minLevel,
        Action<ILogger>? logExtra = null)
    {
        var memory = new MemoryTarget("memory") { Layout = ConsoleLayout };
        using var loggerFactory = CreateLoggerFactory(memory, minLevel);

        var client = new IdentityServicesClient(
            new IdentityServiceClientOptions { EndpointUri = _baseUri, TenantId = "acme" },
            new ServiceClientAccessToken { AccessToken = Token });
        var confirm = A.Fake<IConfirmationService>();
        A.CallTo(() => confirm.Confirm(A<string>._)).Returns(true);
        var command = new ResetPassword(loggerFactory.CreateLogger<ResetPassword>(),
            Options.Create(new OctoToolOptions { TenantId = "acme" }), client, A.Fake<IAuthenticationService>(),
            confirm, FakeSecretConsole.Empty());
        command.CommandArgumentValue.ParseLayer(["-un", "alice", "-p", Password, "-y"]);

        var parser = A.Fake<ICommandParser>();
        A.CallTo(() => parser.ParseAndValidateAsync(A<string?>._))
            .ReturnsLazily(() => command.Execute());
        var contexts = A.Fake<IContextManager>();
        A.CallTo(() => contexts.GetEffectiveContextName()).Returns("test");
        var runnerLogger = loggerFactory.CreateLogger<Runner>();
        logExtra?.Invoke(runnerLogger);

        var exitCode = await new Runner(runnerLogger, parser, contexts, ContextSelection.None).DoActionAsync();
        return (exitCode, string.Join('\n', memory.Logs));
    }

    private static ILoggerFactory CreateLoggerFactory(Target target, NLogLevel minLevel)
    {
        var configuration = Configuration(target, minLevel);
        RedactedLogOutput.Apply(configuration);
        var logFactory = new NLog.LogFactory { Configuration = configuration };
        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
            builder.AddProvider(new NLogLoggerProvider(new NLogProviderOptions(), logFactory));
        });
    }

    private static LoggingConfiguration Configuration(Target target, NLogLevel minLevel)
    {
        var configuration = new LoggingConfiguration();
        configuration.AddRule(minLevel, NLogLevel.Fatal, target);
        return configuration;
    }

    // The plain-text body of the ASP.NET Core developer exception page (Accept is not text/html).
    private static string DeveloperExceptionPage(string authorization, string requestBody) =>
        ServerMessage + "\n" +
        "   at Identity.Users.ResetPassword(String userName) in /src/Users.cs:line 42\n" +
        "   at Microsoft.AspNetCore.Routing.EndpointMiddleware.Invoke(HttpContext httpContext)\n" +
        "\n" +
        "HEADERS\n" +
        "=======\n" +
        "Accept: application/json\n" +
        "Host: 127.0.0.1\n" +
        "Authorization: " + authorization + "\n" +
        "Cookie: session=" + Token + "\n" +
        "Content-Type: application/json\n" +
        "X-Echoed-Body: " + requestBody + "\n";

    private static (HttpListener Listener, string BaseUri) StartListener()
    {
        const int attempts = 20;
        for (var attempt = 1; ; attempt++)
        {
            var uri = $"http://127.0.0.1:{45000 + Random.Shared.Next(0, 2000)}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(uri);
            try
            {
                listener.Start();
                return (listener, uri);
            }
            catch (HttpListenerException) when (attempt < attempts)
            {
                listener.Close();
            }
        }
    }

    // Answers every call like a service on the local stack whose handler threw: 500 with the developer exception
    // page, which echoes the request's Authorization header; the request body is echoed as well.
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

            string body;
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync();
            }

            lock (_requests)
            {
                _requests.Add((context.Request.HttpMethod, context.Request.Url?.AbsolutePath ?? string.Empty));
            }

            var page = DeveloperExceptionPage(context.Request.Headers["Authorization"] ?? "(none)", body) +
                       "\nRequest body: " + body + "\n";
            var bytes = Encoding.UTF8.GetBytes(page);
            context.Response.StatusCode = 500;
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }
}
