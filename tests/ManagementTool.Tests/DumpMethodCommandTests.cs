using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FakeItEasy;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Operations;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#8446: <c>Dump</c> runs through the asynchronous CK method <c>System.Bot/TenantRepository.Dump-1</c> in the
///     tenant of the context, logs the operation's progress and downloads the job of the result; it falls back to the job
///     API when the service lacks the method and for another tenant. <c>GetOperation</c> / <c>ListOperations</c> read
///     <c>runtime.operations</c>. The commands run with the real SDK <see cref="TenantClient" /> against a local listener
///     that answers with responses recorded from the asset repository on test-2 (scratch tenant with System.Bot 3.7.0);
///     every document is validated against the schema snapshot of that tenant.
/// </summary>
public sealed class DumpMethodCommandTests : IDisposable
{
    private const string OperationId = "cff5f253aebe408c9b170bcf4922975b";
    private const string JobId = "6af38cc34f052289b00e91d6";

    private readonly GraphQlListener _service = new();
    private readonly IBotServicesClient _bot = A.Fake<IBotServicesClient>();
    private readonly IAuthenticationService _auth = A.Fake<IAuthenticationService>();
    private readonly IConsoleService _console = A.Fake<IConsoleService>();
    private readonly List<string> _consoleLines = [];

    public DumpMethodCommandTests()
    {
        A.CallTo(() => _bot.ServiceUri).Returns(new Uri("https://bots.example/"));
        A.CallTo(() => _bot.StartDumpRepositoryAsync(A<string>._, A<bool>._)).Returns(new JobResponseDto("legacy-job"));
        A.CallTo(() => _bot.GetImportJobStatus(A<string>._)).Returns(new JobDto { Status = "Succeeded" });
        A.CallTo(() => _console.WriteLine(A<string>._)).Invokes((string line) => _consoleLines.Add(line));
    }

    public void Dispose() => _service.Dispose();

    [Fact]
    public void DumpDocument_IsTheTextTheSdkGeneratorEmits()
    {
        // octo-sdk AsyncMethodClientTests.ExpectedDumpDocument (generator output for System.Bot 3.7.0).
        Assert.Equal(
            "mutation SystemBotTenantRepositoryDump($input: SystemBotTenantRepositoryDumpMethodInput, $idempotencyKey: String) " +
            "{ runtime { systemBotTenantRepositorys { dump(input: $input, idempotencyKey: $idempotencyKey) " +
            "{ invocationId operation { operationId acceptedAtUtc retryAfterSeconds expiresAtUtc } } } } }",
            TenantRepositoryDumpClient.DumpDocument);
    }

    [Fact]
    public async Task Dump_InTheContextTenant_RunsTheMethod_LogsProgress_AndDownloadsTheResultJob()
    {
        _service.RespondInOrder(Recorded("dump-accepted"), Recorded("operation-running"), Recorded("operation-running"),
            Recorded("operation-succeeded"));
        var logger = new RecordingLogger<DumpTenant>();
        var command = Dump(logger, "-tid", "Customer", "-f", "backup.octobak.zip", "-iad");

        await command.Execute();

        A.CallTo(() => _bot.DownloadDumpToFileAsync("customer", JobId, "backup.octobak.zip", A<Action<long>>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _bot.StartDumpRepositoryAsync(A<string>._, A<bool>._)).MustNotHaveHappened();
        A.CallTo(() => _bot.GetImportJobStatus(A<string>._)).MustNotHaveHappened();
        Assert.Equal(4, _service.Bodies.Count);
        Assert.Contains("\"input\":{\"includeArchiveData\":true}", _service.Bodies[0], StringComparison.Ordinal);
        Assert.All(_service.Paths, p => Assert.Equal("/tenants/customer/GraphQL", p));
        var messages = logger.Entries.Select(e => e.Message).ToList();
        Assert.Contains($"Dump operation '{OperationId}' accepted (System.Bot/TenantRepository.Dump-1)", messages);
        Assert.Contains($"Dump operation '{OperationId}': RUNNING, stage QUEUED, 0 % (jobId={JobId})", messages);
        Assert.Contains($"Dump operation '{OperationId}': SUCCEEDED, stage STORING_ARTIFACT, 80 % (jobId={JobId})", messages);
        Assert.Contains($"Dump operation '{OperationId}' succeeded with job id '{JobId}'", messages);
        // the second, unchanged RUNNING read is not logged again
        Assert.Single(messages, m => m.Contains("RUNNING", StringComparison.Ordinal));
        // the tenant client's token is refreshed before the call and before every read (long dumps)
        A.CallTo(() => _auth.EnsureAuthenticated(A<IServiceClientAccessToken>._)).MustHaveHappened(4, Times.OrMore);
    }

    [Fact]
    public async Task Dump_EverySentDocument_IsValidAgainstTheSchemaOfATenantWithSystemBot37()
    {
        _service.RespondInOrder(Recorded("dump-accepted"), Recorded("operation-succeeded"));

        await Dump(new RecordingLogger<DumpTenant>(), "-tid", "customer", "-f", "backup.tar.gz").Execute();

        Assert.Equal(2, _service.Bodies.Count);
        foreach (var body in _service.Bodies)
        {
            using var request = JsonDocument.Parse(body);
            var errors = await AssetRepositoryGraphQlSchema.ValidateAgainstSystemBotAsync(
                request.RootElement.GetProperty("query").GetString()!,
                request.RootElement.GetProperty("variables").GetRawText());
            Assert.True(errors.Count == 0, string.Join("; ", errors));
        }
    }

    [Fact]
    public async Task Dump_AnotherTenant_UsesTheJobApi_WithoutAMethodCall()
    {
        var logger = new RecordingLogger<DumpTenant>();

        await Dump(logger, "-tid", "child", "-f", "backup.tar.gz").Execute();

        Assert.Empty(_service.Bodies);
        A.CallTo(() => _bot.StartDumpRepositoryAsync("child", false)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _bot.DownloadDumpToFileAsync("child", "legacy-job", "backup.tar.gz", A<Action<long>>._))
            .MustHaveHappenedOnceExactly();
        Assert.Contains(logger.Entries, e => e.Message.Contains("is not the tenant of the context", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Dump_AssetRepositoryWithoutTheMethod_FallsBackToTheJobApi()
    {
        // The validation answer of an asset repository / tenant without TenantRepository.Dump-1 (shape recorded on test-2).
        _service.RespondInOrder("""
            {"errors":[{"message":"Unknown type SystemBotTenantRepositoryDumpMethodInput.","locations":[{"line":1,"column":48}],"extensions":{"code":"KNOWN_TYPE_NAMES","codes":["KNOWN_TYPE_NAMES"],"number":"5.5.1.2"}},
                       {"message":"Cannot query field 'systemBotTenantRepositorys' on type 'Runtime'.","locations":[{"line":1,"column":128}],"extensions":{"code":"FIELDS_ON_CORRECT_TYPE","codes":["FIELDS_ON_CORRECT_TYPE"],"number":"5.3.1"}}]}
            """);
        var logger = new RecordingLogger<DumpTenant>();

        await Dump(logger, "-tid", "customer", "-f", "backup.tar.gz", "-iad").Execute();

        Assert.Single(_service.Bodies);
        A.CallTo(() => _bot.StartDumpRepositoryAsync("customer", true)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _bot.DownloadDumpToFileAsync("customer", "legacy-job", "backup.tar.gz", A<Action<long>>._))
            .MustHaveHappenedOnceExactly();
        Assert.Contains(logger.Entries, e => e.Message.StartsWith(
            "The dump method System.Bot/TenantRepository.Dump-1 is not available (the asset repository does not know it",
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task Dump_NoLiveHandler_FallsBackToTheJobApi()
    {
        _service.RespondInOrder(MethodError("METHOD_HANDLER_NOT_AVAILABLE", ("reason", "no-live-handler")));

        await Dump(new RecordingLogger<DumpTenant>(), "-tid", "customer", "-f", "backup.tar.gz").Execute();

        A.CallTo(() => _bot.StartDumpRepositoryAsync("customer", false)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Dump_Forbidden_IsThrown_WithoutFallback()
    {
        _service.RespondInOrder(MethodError("METHOD_FORBIDDEN", ("reason", "not-authorized")));

        var e = await Assert.ThrowsAsync<CkMethodCallException>(() =>
            Dump(new RecordingLogger<DumpTenant>(), "-tid", "customer", "-f", "backup.tar.gz").Execute());

        Assert.Equal("METHOD_FORBIDDEN", e.Code);
        A.CallTo(() => _bot.StartDumpRepositoryAsync(A<string>._, A<bool>._)).MustNotHaveHappened();
        A.CallTo(() => _bot.DownloadDumpToFileAsync(A<string>._, A<string>._, A<string>._, A<Action<long>>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Dump_OperationFailsWithADeclaredError_ReportsCodeAndParameters_AndDownloadsNothing()
    {
        _service.RespondInOrder(Recorded("dump-accepted"), Failed("DUMP_FAILED", ("jobId", JobId)));

        var e = await Assert.ThrowsAsync<ServiceClientException>(() =>
            Dump(new RecordingLogger<DumpTenant>(), "-tid", "customer", "-f", "backup.tar.gz").Execute());

        Assert.Equal($"DUMP_FAILED: The dump job failed; the job status (system/v1/jobs) shows its error. (jobId={JobId})",
            e.Message);
        A.CallTo(() => _bot.DownloadDumpToFileAsync(A<string>._, A<string>._, A<string>._, A<Action<long>>._))
            .MustNotHaveHappened();
        A.CallTo(() => _bot.StartDumpRepositoryAsync(A<string>._, A<bool>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Dump_HandlerLost_IsThrownAsMethodOutcomeUnknown()
    {
        _service.RespondInOrder(Recorded("dump-accepted"), Failed("METHOD_OUTCOME_UNKNOWN", ("reason", "handler-lost")));

        var e = await Assert.ThrowsAsync<CkMethodCallException>(() =>
            Dump(new RecordingLogger<DumpTenant>(), "-tid", "customer", "-f", "backup.tar.gz").Execute());

        Assert.Equal("METHOD_OUTCOME_UNKNOWN", e.Code);
        Assert.Equal(
            "The CK method call 'System.Bot/TenantRepository.Dump-1' failed: METHOD_OUTCOME_UNKNOWN (reason=handler-lost)",
            CkMethodErrorText.Format(e));
    }

    [Fact]
    public async Task GetOperation_PrintsStateProgressAndResult()
    {
        _service.RespondInOrder(Recorded("operation-succeeded"));

        await Get("-id", OperationId).Execute();

        Assert.Contains($"Operation:  {OperationId}", _consoleLines);
        Assert.Contains("Method:     System.Bot/TenantRepository.Dump-1", _consoleLines);
        Assert.Contains($"State:      SUCCEEDED, stage STORING_ARTIFACT, 80 % (jobId={JobId})", _consoleLines);
        Assert.Contains(_consoleLines, l => l.StartsWith("Result:     {\"jobId\":", StringComparison.Ordinal));
        Assert.Contains(_consoleLines, l => l.StartsWith("Expires:    2026-11-17", StringComparison.Ordinal));
        var body = Assert.Single(_service.Bodies);
        Assert.Empty(await AssetRepositoryGraphQlSchema.ValidateAgainstSystemBotAsync(Query(body), Variables(body)));
    }

    [Fact]
    public async Task GetOperation_Json_WritesTheOperation()
    {
        _service.RespondInOrder(Recorded("operation-succeeded"));

        await Get("-id", OperationId, "-j").Execute();

        using var json = JsonDocument.Parse(Assert.Single(_consoleLines));
        Assert.Equal("Succeeded", json.RootElement.GetProperty("state").GetString());
        Assert.Equal(JobId, json.RootElement.GetProperty("result").GetProperty("jobId").GetString());
    }

    [Fact]
    public async Task GetOperation_Wait_PollsUntilFinal()
    {
        _service.RespondInOrder(Recorded("operation-running"), Recorded("operation-succeeded"));
        var logger = new RecordingLogger<GetOperation>();
        var command = Get(logger, "-id", OperationId, "-w");

        await command.Execute();

        Assert.Equal(2, _service.Bodies.Count);
        Assert.Contains(logger.Entries, e => e.Message == $"Operation '{OperationId}': RUNNING, stage QUEUED, 0 % (jobId={JobId})");
        Assert.Contains($"State:      SUCCEEDED, stage STORING_ARTIFACT, 80 % (jobId={JobId})", _consoleLines);
    }

    [Fact]
    public async Task GetOperation_WaitOnAFailedOperation_EndsWithAnError()
    {
        _service.RespondInOrder(Failed("DUMP_INTERRUPTED", ("jobId", JobId)));

        var e = await Assert.ThrowsAsync<ServiceClientException>(() => Get("-id", OperationId, "-w").Execute());

        Assert.Equal($"Operation '{OperationId}' failed: DUMP_INTERRUPTED (jobId={JobId})", e.Message);
        Assert.Contains($"Error:      DUMP_INTERRUPTED (jobId={JobId})", _consoleLines);
    }

    [Fact]
    public async Task GetOperation_NotFound_IsTheRecordedOperationNotFound()
    {
        _service.RespondInOrder(Recorded("operation-not-found"));

        var e = await Assert.ThrowsAsync<CkOperationQueryException>(() => Get("-id", "unknown").Execute());

        Assert.Equal("OPERATION_NOT_FOUND", e.Code);
        Assert.Empty(_consoleLines);
    }

    [Fact]
    public async Task ListOperations_SendsTheFilter_AndPrintsOneRowPerOperation()
    {
        _service.RespondInOrder(Recorded("operation-list"));

        await List("-m", "System.Bot/TenantRepository.Dump-1", "-s", "succeeded", "-since", "2026-11-10T00:00:00Z",
            "-t", "10", "-a").Execute();

        var body = Assert.Single(_service.Bodies);
        var variables = Variables(body);
        Assert.Contains("\"state\":\"SUCCEEDED\"", variables, StringComparison.Ordinal);
        Assert.Contains("\"take\":10", variables, StringComparison.Ordinal);
        Assert.Contains("\"allOwners\":true", variables, StringComparison.Ordinal);
        Assert.Contains("\"since\":\"2026-11-10T00:00:00Z\"", variables, StringComparison.Ordinal);
        Assert.Empty(await AssetRepositoryGraphQlSchema.ValidateAgainstSystemBotAsync(Query(body), variables));
        Assert.Equal("2 of 2 operation(s)", _consoleLines[0]);
        Assert.Equal(3, _consoleLines.Count);
        Assert.All(_consoleLines.Skip(1), l => Assert.Contains("System.Bot/TenantRepository.Dump-1  SUCCEEDED", l, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Running", CkOperationState.Running)]
    [InlineData("FAILED", CkOperationState.Failed)]
    [InlineData("accepted", CkOperationState.Accepted)]
    public void ListOperations_ParsesStates(string value, CkOperationState expected) =>
        Assert.Equal(expected, ListOperations.ParseState(value));

    [Theory]
    [InlineData("Unknown")]
    [InlineData("done")]
    [InlineData("7")]
    public void ListOperations_RejectsOtherStates(string value) =>
        Assert.Throws<ToolException>(() => ListOperations.ParseState(value));

    private DumpTenant Dump(RecordingLogger<DumpTenant> logger, params string[] args)
    {
        var command = new DumpTenant(logger, Options(), _bot, _auth, TenantClient()) { TimeProvider = new ImmediateTimeProvider() };
        command.CommandArgumentValue.ParseLayer(args);
        return command;
    }

    private GetOperation Get(params string[] args) => Get(new RecordingLogger<GetOperation>(), args);

    private GetOperation Get(RecordingLogger<GetOperation> logger, params string[] args)
    {
        var command = new GetOperation(logger, Options(), TenantClient(), _auth, _console)
            { TimeProvider = new ImmediateTimeProvider() };
        command.CommandArgumentValue.ParseLayer(args);
        return command;
    }

    private ListOperations List(params string[] args)
    {
        var command = new ListOperations(new RecordingLogger<ListOperations>(), Options(), TenantClient(), _auth, _console);
        command.CommandArgumentValue.ParseLayer(args);
        return command;
    }

    private static IOptions<OctoToolOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new OctoToolOptions { TenantId = "customer" });

    private TenantClient TenantClient()
    {
        var token = A.Fake<ITenantClientAccessToken>();
        A.CallTo(() => token.AccessToken).Returns(null);
        return new TenantClient(new TenantClientOptions { EndpointUri = _service.BaseUrl, TenantId = "customer" }, token);
    }

    private static string Recorded(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"assetrepo-bot-answer-{name}.json"));

    private static string Query(string body)
    {
        using var request = JsonDocument.Parse(body);
        return request.RootElement.GetProperty("query").GetString()!;
    }

    private static string Variables(string body)
    {
        using var request = JsonDocument.Parse(body);
        return request.RootElement.GetProperty("variables").GetRawText();
    }

    private static string MethodError(string code, params (string Name, string Value)[] parameters) =>
        JsonSerializer.Serialize(new
        {
            errors = new[]
            {
                new
                {
                    message = $"The CK method call failed: {code}",
                    extensions = new
                    {
                        code,
                        parameters = parameters.ToDictionary(p => p.Name, p => p.Value)
                    }
                }
            },
            data = new { runtime = new { systemBotTenantRepositorys = new { dump = (object?)null } } }
        });

    /// <summary>The recorded SUCCEEDED answer turned into a FAILED one with <paramref name="code" />.</summary>
    private static string Failed(string code, params (string Name, string Value)[] parameters)
    {
        using var answer = JsonDocument.Parse(Recorded("operation-succeeded"));
        var item = answer.RootElement.GetProperty("data").GetProperty("runtime").GetProperty("operations")
            .GetProperty("items")[0];
        var failed = new Dictionary<string, object?>
        {
            ["operationId"] = item.GetProperty("operationId").GetString(),
            ["methodId"] = item.GetProperty("methodId").GetString(),
            ["invocationId"] = item.GetProperty("invocationId").GetString(),
            ["state"] = "FAILED",
            ["progress"] = null,
            ["result"] = null,
            ["error"] = new
            {
                code,
                parameters = parameters.Select(p => new { name = p.Name, value = p.Value }).ToArray(),
                details = Array.Empty<object>()
            },
            ["acceptedAtUtc"] = item.GetProperty("acceptedAtUtc").GetString(),
            ["startedAtUtc"] = item.GetProperty("startedAtUtc").GetString(),
            ["completedAtUtc"] = item.GetProperty("completedAtUtc").GetString(),
            ["expiresAtUtc"] = item.GetProperty("expiresAtUtc").GetString()
        };
        return JsonSerializer.Serialize(new
        {
            data = new { runtime = new { operations = new { totalCount = 1, items = new[] { failed } } } }
        });
    }

    /// <summary>Lets every delay elapse at once.</summary>
    private sealed class ImmediateTimeProvider : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            new ImmediateTimer(callback, state);

        private sealed class ImmediateTimer : ITimer
        {
            public ImmediateTimer(TimerCallback callback, object? state) =>
                ThreadPool.QueueUserWorkItem(_ => callback(state));

            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>A local GraphQL endpoint answering with queued bodies in order (then <c>{}</c>) and recording requests.</summary>
    private sealed class GraphQlListener : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Queue<string> _queued = new();
        private readonly List<string> _bodies = [];
        private readonly List<string> _paths = [];
        private readonly Lock _sync = new();

        public GraphQlListener()
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

        public IReadOnlyList<string> Bodies
        {
            get
            {
                lock (_sync)
                {
                    return _bodies.ToArray();
                }
            }
        }

        public IReadOnlyList<string> Paths
        {
            get
            {
                lock (_sync)
                {
                    return _paths.ToArray();
                }
            }
        }

        public void RespondInOrder(params string[] bodies)
        {
            lock (_sync)
            {
                foreach (var body in bodies)
                {
                    _queued.Enqueue(body);
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
                string response;
                lock (_sync)
                {
                    _bodies.Add(body);
                    _paths.Add(context.Request.Url!.AbsolutePath);
                    response = _queued.Count > 0 ? _queued.Dequeue() : "{}";
                }

                var bytes = Encoding.UTF8.GetBytes(response);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }
    }
}
