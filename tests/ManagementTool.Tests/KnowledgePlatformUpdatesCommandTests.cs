using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Knowledge;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.General.Context;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     <c>ListKnowledgePlatformUpdates</c> and <c>AdoptKnowledgePlatformUpdates</c>: both call the knowledge
///     platform-update tools of the MCP service on the tenant route with the user's token, so the rules are the tool's
///     (edited pages untouched). <c>--dryRun</c> never calls the adopt tool. The token never reaches output.
/// </summary>
public sealed class KnowledgePlatformUpdatesCommandTests
{
    private const string Tenant = "ai-sandbox";
    private const string McpUrl = "https://mcp.example.test/";

    private static readonly string UserToken = Jwt(new { sub = "686a6215fd3e5972ff5693fd", tenant_id = Tenant });

    private readonly IMcpToolClient _mcp = A.Fake<IMcpToolClient>();
    private readonly IConsoleService _console = A.Fake<IConsoleService>();
    private readonly List<string> _printed = [];
    private readonly List<(Uri Endpoint, string Token, string Tool, JsonObject Arguments)> _calls = [];

    private const string ListAnswer = """
        {"isSuccess":true,"tenantId":"ai-sandbox","updates":[
          {"slug":"app-index","state":"Adoptable","currentVersion":2,"newerVersion":3,"blueprint":"System.Ai.Builder-1.2.0"},
          {"slug":"admin-cli-basics","state":"Available","currentVersion":5,"newerVersion":4,"blueprint":"System.Ai.Builder-1.2.0","dismissedVersion":3}
        ],"truncated":false}
        """;

    private const string AdoptAnswer = """
        {"isSuccess":true,"tenantId":"ai-sandbox","runRef":"rollout-1",
         "adopted":[{"isSuccess":true,"slug":"app-index","fromVersion":2,"toVersion":3,"blueprint":"System.Ai.Builder-1.2.0"}],
         "skipped":[{"isSuccess":false,"slug":"app-http","fromVersion":1,"toVersion":2,"reason":"VersionConflict"}],
         "available":[{"slug":"admin-cli-basics","state":"Available","currentVersion":5,"newerVersion":4}],
         "truncated":false}
        """;

    public KnowledgePlatformUpdatesCommandTests()
    {
        A.CallTo(() => _console.WriteLine(A<string>._)).Invokes((string line) => _printed.Add(line));
        A.CallTo(() => _mcp.CallToolAsync(A<Uri>._, A<string>._, A<string>._, A<JsonObject>._, A<CancellationToken>._))
            .ReturnsLazily((Uri endpoint, string token, string tool, JsonObject arguments, CancellationToken _) =>
            {
                _calls.Add((endpoint, token, tool, arguments));
                return Task.FromResult<JsonNode?>(JsonNode.Parse(tool == KnowledgePlatformUpdatesMcp.AdoptTool ? AdoptAnswer : ListAnswer));
            });
    }

    private static string Jwt(object payload)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64("{\"alg\":\"none\"}")}.{B64(JsonSerializer.Serialize(payload))}.sig";
    }

    private static OctoToolOptions Options(string? mcpUrl = McpUrl) => new() { TenantId = Tenant, McpServiceUrl = mcpUrl };

    private Task<RecordingLogger<AdoptKnowledgePlatformUpdatesCommand>> RunAdopt(OctoToolOptions options, params string[] args) =>
        RunAdoptAs(UserToken, A.Fake<ITenantTokenProvider>(), options, args);

    private async Task<RecordingLogger<AdoptKnowledgePlatformUpdatesCommand>> RunAdoptAs(string contextToken,
        ITenantTokenProvider? tokenProvider, OctoToolOptions options, params string[] args)
    {
        var logger = new RecordingLogger<AdoptKnowledgePlatformUpdatesCommand>();
        var command = new AdoptKnowledgePlatformUpdatesCommand(logger, Microsoft.Extensions.Options.Options.Create(options),
            A.Fake<IAssetServicesClient>(), A.Fake<IAuthenticationService>(),
            Microsoft.Extensions.Options.Options.Create(new OctoToolAuthenticationOptions { AccessToken = contextToken }),
            _mcp, _console, tokenProvider, new FixedTime(new DateTimeOffset(2026, 11, 24, 15, 30, 0, TimeSpan.Zero)));
        command.CommandArgumentValue.ParseLayer(args);
        await command.Execute();
        return logger;
    }

    private async Task<RecordingLogger<ListKnowledgePlatformUpdatesCommand>> RunList(OctoToolOptions options, params string[] args)
    {
        var logger = new RecordingLogger<ListKnowledgePlatformUpdatesCommand>();
        var command = new ListKnowledgePlatformUpdatesCommand(logger, Microsoft.Extensions.Options.Options.Create(options),
            A.Fake<IAssetServicesClient>(), A.Fake<IAuthenticationService>(),
            Microsoft.Extensions.Options.Options.Create(new OctoToolAuthenticationOptions { AccessToken = UserToken }),
            _mcp, _console, A.Fake<ITenantTokenProvider>());
        command.CommandArgumentValue.ParseLayer(args);
        await command.Execute();
        return logger;
    }

    [Fact]
    public async Task Adopt_CallsTheAdoptToolOnTheTenantRouteWithTheUsersToken_AndPrintsEveryPage()
    {
        await RunAdopt(Options(), "-rr", "rollout-1");

        var call = Assert.Single(_calls);
        Assert.Equal(KnowledgePlatformUpdatesMcp.AdoptTool, call.Tool);
        Assert.Equal(new Uri("https://mcp.example.test/ai-sandbox/mcp"), call.Endpoint);
        Assert.Equal(UserToken, call.Token);
        Assert.Equal("rollout-1", call.Arguments["runRef"]!.GetValue<string>());
        Assert.Equal(Tenant, call.Arguments["tenantId"]!.GetValue<string>());
        Assert.Contains(_printed, l => l.Contains("adopted 1 page(s), skipped 1; 1 edited page(s) keep their content"));
        Assert.Contains(_printed, l => l.Contains("ADOPTED") && l.Contains("app-index") && l.Contains("v2 -> v3"));
        Assert.Contains(_printed, l => l.Contains("SKIPPED") && l.Contains("VersionConflict"));
        Assert.Contains(_printed, l => l.Contains("AVAILABLE") && l.Contains("admin-cli-basics"));
    }

    [Fact]
    public async Task Adopt_DryRun_CallsOnlyTheListTool_AndWritesNothing()
    {
        await RunAdopt(Options(), "--dryRun");

        var call = Assert.Single(_calls);
        Assert.Equal(KnowledgePlatformUpdatesMcp.ListTool, call.Tool);
        Assert.False(call.Arguments.ContainsKey("runRef"));
        Assert.Contains(_printed, l => l.Contains("1 unedited page(s) to adopt, 1 edited page(s)"));
        Assert.Contains(_printed, l => l.StartsWith("Dry run: nothing was written", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Adopt_WithoutRunRef_UsesOctoCliAndTheUtcTime()
    {
        await RunAdopt(Options());

        Assert.Equal("octo-cli:2026-11-24T15:30:00Z", Assert.Single(_calls).Arguments["runRef"]!.GetValue<string>());
    }

    [Fact]
    public async Task Adopt_RunRefLongerThan200Characters_IsRefusedBeforeAnyCall()
    {
        await Assert.ThrowsAsync<ToolException>(() => RunAdopt(Options(), "-rr", new string('x', 201)));
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task Adopt_RefusedByTheTool_FailsWithItsCodeAndMessage()
    {
        A.CallTo(() => _mcp.CallToolAsync(A<Uri>._, A<string>._, A<string>._, A<JsonObject>._, A<CancellationToken>._))
            .Returns(Task.FromResult<JsonNode?>(JsonNode.Parse(
                """{"isSuccess":false,"errorCode":"ModelTooOld","errorMessage":"Needs System.Ai 4.10.0"}""")));

        var ex = await Assert.ThrowsAsync<ToolException>(() => RunAdopt(Options()));
        Assert.Contains("ModelTooOld", ex.Message);
        Assert.Contains("Needs System.Ai 4.10.0", ex.Message);
    }

    [Fact]
    public async Task NoMcpServiceUrl_IsRefusedWithTheWayToSetIt_BeforeAnyCall()
    {
        var ex = await Assert.ThrowsAsync<ToolException>(() => RunAdopt(Options(mcpUrl: null)));
        Assert.Contains("AddContext", ex.Message);
        Assert.Contains("-msu", ex.Message);
        Assert.Empty(_calls);
    }

    [Theory]
    [InlineData("-msu")]
    [InlineData("--mcpServiceUri")]
    public async Task APerCallMcpServiceUri_IsNotAccepted_SoTheTokenGoesOnlyToTheContextsService(string flag)
    {
        // There is no per-call override: an instruction such as "-msu http://elsewhere/" cannot redirect the user's token.
        // The commands declare no such argument, so the word never reaches the endpoint; the call goes to the context's URL.
        await RunList(Options(), flag, "http://other.example.test/mcp");
        await RunAdopt(Options(), flag, "http://other.example.test/mcp");

        Assert.Equal(2, _calls.Count);
        Assert.All(_calls, c => Assert.Equal(new Uri("https://mcp.example.test/ai-sandbox/mcp"), c.Endpoint));
    }

    [Fact]
    public async Task AnHttpMcpServiceUrlOnTheContext_IsRefusedBeforeAnyCall_ExceptForLocalhost()
    {
        var ex = await Assert.ThrowsAsync<ToolException>(() => RunList(Options("http://mcp.example.test/")));
        Assert.Contains("https", ex.Message);
        Assert.Empty(_calls);

        await RunList(Options("http://localhost:5017/"));
        Assert.Equal(new Uri("http://localhost:5017/ai-sandbox/mcp"), Assert.Single(_calls).Endpoint);
    }

    [Fact]
    public async Task Adopt_FailedPartWay_PrintsThePagesAlreadySwitched_AndDoesNotClaimNothingChanged()
    {
        A.CallTo(() => _mcp.CallToolAsync(A<Uri>._, A<string>._, A<string>._, A<JsonObject>._, A<CancellationToken>._))
            .Returns(Task.FromResult<JsonNode?>(JsonNode.Parse("""
                {"isSuccess":false,"errorCode":"Unexpected","errorMessage":"database went away","tenantId":"ai-sandbox",
                 "adopted":[{"isSuccess":true,"slug":"app-index","fromVersion":2,"toVersion":3,"blueprint":"System.Ai.Builder-1.2.0"}],
                 "skipped":[],"available":[],"truncated":false}
                """)));

        var ex = await Assert.ThrowsAsync<ToolException>(() => RunAdopt(Options(), "-rr", "rollout-1"));

        Assert.Contains(_printed, l => l.Contains("ADOPTED") && l.Contains("app-index"));
        Assert.Contains("database went away", ex.Message);
        Assert.Contains("1 page(s) were switched before the failure", ex.Message);
        Assert.DoesNotContain("Nothing was changed", ex.Message);
    }

    [Fact]
    public async Task Adopt_FailedPartWay_WithJson_PrintsTheAnswerBeforeTheError()
    {
        A.CallTo(() => _mcp.CallToolAsync(A<Uri>._, A<string>._, A<string>._, A<JsonObject>._, A<CancellationToken>._))
            .Returns(Task.FromResult<JsonNode?>(JsonNode.Parse("""
                {"isSuccess":false,"errorMessage":"database went away",
                 "adopted":[{"isSuccess":true,"slug":"app-index","fromVersion":2,"toVersion":3}],"skipped":[],"available":[]}
                """)));

        await Assert.ThrowsAsync<ToolException>(() => RunAdopt(Options(), "--json"));

        Assert.Contains(_printed, l => l.Contains("\"adopted\"") && l.Contains("app-index"));
    }

    [Fact]
    public async Task Adopt_RefusedWithoutAnySwitchedPage_SaysNothingWasChanged()
    {
        A.CallTo(() => _mcp.CallToolAsync(A<Uri>._, A<string>._, A<string>._, A<JsonObject>._, A<CancellationToken>._))
            .Returns(Task.FromResult<JsonNode?>(JsonNode.Parse(
                """{"isSuccess":false,"errorCode":"ModelTooOld","errorMessage":"Needs System.Ai 4.10.0","adopted":[],"skipped":[]}""")));

        var ex = await Assert.ThrowsAsync<ToolException>(() => RunAdopt(Options()));
        Assert.Contains("Nothing was changed", ex.Message);
        Assert.Empty(_printed);
    }

    [Fact]
    public async Task OtherTenant_SendsTheExchangedTokenOfThatTenant_NotTheContextToken()
    {
        var rootToken = Jwt(new { sub = "686a6215fd3e5972ff5693fd", tenant_id = "octosystem" });
        var provider = A.Fake<ITenantTokenProvider>();
        A.CallTo(() => provider.GetAccessTokenAsync(Tenant, rootToken, A<CancellationToken>._))
            .Returns(Task.FromResult("exchanged-token-for-ai-sandbox"));

        await RunAdoptAs(rootToken, provider, Options(), "-tid", Tenant, "-rr", "rollout-1");

        var call = Assert.Single(_calls);
        Assert.Equal("exchanged-token-for-ai-sandbox", call.Token);
        Assert.Equal(new Uri("https://mcp.example.test/ai-sandbox/mcp"), call.Endpoint);
    }

    [Fact]
    public async Task OtherTenant_WithoutATokenProvider_IsRefused_AndNeverFallsBackToTheContextToken()
    {
        var rootToken = Jwt(new { sub = "686a6215fd3e5972ff5693fd", tenant_id = "octosystem" });

        var ex = await Assert.ThrowsAsync<ToolException>(() => RunAdoptAs(rootToken, null, Options(), "-tid", Tenant));

        Assert.Contains(Tenant, ex.Message);
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task OtherTenant_ExchangeDenied_IsRefusedWithTheWayOut_AndNeverFallsBackToTheContextToken()
    {
        var rootToken = Jwt(new { sub = "686a6215fd3e5972ff5693fd", tenant_id = "octosystem" });
        var provider = A.Fake<ITenantTokenProvider>();
        A.CallTo(() => provider.GetAccessTokenAsync(Tenant, rootToken, A<CancellationToken>._))
            .Throws(new TenantTokenExchangeDeniedException(Tenant, TenantTokenDenialCause.Unknown));

        var ex = await Assert.ThrowsAsync<ToolException>(() => RunAdoptAs(rootToken, provider, Options(), "-tid", Tenant));

        Assert.Contains("ProvisionCurrentUser", ex.Message);
        Assert.Empty(_calls);
    }

    [Theory]
    [InlineData("https://mcp.example.test/")]
    [InlineData("http://localhost:5017/")]
    [InlineData("http://127.0.0.1:5017/")]
    [InlineData("http://[::1]:5017/")]
    public void McpServiceUrlRule_AcceptsHttpsAndLoopbackHttp(string url) => McpServiceUrlRule.Check(url);

    [Theory]
    [InlineData("http://mcp.example.test/")]
    [InlineData("http://10.0.0.5:5017/")]
    [InlineData("http://localhost.example.test/")]
    [InlineData("ftp://mcp.example.test/")]
    [InlineData("mcp.example.test")]
    public void McpServiceUrlRule_RefusesPlainHttpToAnotherHostAndNonHttpUrls(string url) =>
        Assert.Throws<ToolException>(() => McpServiceUrlRule.Check(url));

    [Fact]
    public async Task AddContext_RefusesAnHttpMcpServiceUrl_AndSavesNoContext()
    {
        var contexts = A.Fake<IContextManager>();
        var command = new AddContextCommand(new RecordingLogger<AddContextCommand>(),
            Microsoft.Extensions.Options.Options.Create(new OctoToolOptions()), contexts);
        command.CommandArgumentValue.ParseLayer(["-n", "test", "-msu", "http://mcp.example.test/"]);

        await Assert.ThrowsAsync<ToolException>(() => command.Execute());

        A.CallTo(() => contexts.AddOrUpdateContext(A<string>._, A<ContextEntry>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task AddContext_StoresAnHttpsMcpServiceUrl()
    {
        var contexts = A.Fake<IContextManager>();
        ContextEntry? saved = null;
        A.CallTo(() => contexts.AddOrUpdateContext("test", A<ContextEntry>._))
            .Invokes((string _, ContextEntry entry) => saved = entry);
        var command = new AddContextCommand(new RecordingLogger<AddContextCommand>(),
            Microsoft.Extensions.Options.Options.Create(new OctoToolOptions()), contexts);
        command.CommandArgumentValue.ParseLayer(["-n", "test", "-msu", "https://mcp.example.test/"]);

        await command.Execute();

        Assert.Equal("https://mcp.example.test/", saved?.OctoToolOptions?.McpServiceUrl);
    }

    [Fact]
    public async Task List_PrintsStateVersionsAndDismissedVersion_AndChangesNothing()
    {
        await RunList(Options(), "-tid", "AI-Sandbox");

        var call = Assert.Single(_calls);
        Assert.Equal(KnowledgePlatformUpdatesMcp.ListTool, call.Tool);
        Assert.Equal(Tenant, call.Arguments["tenantId"]!.GetValue<string>());
        Assert.Contains(_printed, l => l.Contains("Adoptable") && l.Contains("app-index") && l.Contains("v2 -> v3"));
        Assert.Contains(_printed, l => l.Contains("Available") && l.Contains("(dismissed v3)"));
    }

    [Fact]
    public async Task TheTokenIsNeverLoggedOrPrinted_AtAnyLogLevel()
    {
        // RecordingLogger is enabled for every level, as with Trace in nlog.config.
        var adoptLog = await RunAdopt(Options(), "-rr", "rollout-1");
        var dryLog = await RunAdopt(Options(), "--dryRun");
        var listLog = await RunList(Options(), "--json");

        var all = adoptLog.Entries.Concat(dryLog.Entries).Concat(listLog.Entries).Select(e => e.Message).Concat(_printed).ToList();
        Assert.NotEmpty(all);
        Assert.DoesNotContain(all, line => line.Contains(UserToken, StringComparison.Ordinal));
        Assert.DoesNotContain(all, line => line.Contains("Bearer", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("https://mcp.example.test/", "https://mcp.example.test/t1/mcp")]
    [InlineData("https://mcp.example.test/mcp", "https://mcp.example.test/t1/mcp")]
    [InlineData("https://mcp.example.test/mcp/", "https://mcp.example.test/t1/mcp")]
    [InlineData("https://localhost:5017", "https://localhost:5017/t1/mcp")]
    [InlineData("https://gw.example.test/octo-mcp/", "https://gw.example.test/octo-mcp/t1/mcp")]
    public void TenantEndpoint_IsTheTenantRouteOfTheService(string baseUrl, string expected)
    {
        Assert.Equal(new Uri(expected), McpToolClient.TenantEndpoint(baseUrl, "t1"));
    }

    [Fact]
    public void TenantEndpoint_RefusesANonHttpUrl()
    {
        Assert.Throws<ToolException>(() => McpToolClient.TenantEndpoint("ftp://mcp.example.test/", "t1"));
    }
}

/// <summary>The MCP Streamable HTTP client of the CLI against a stub server: session, SSE answers, errors without the token.</summary>
public sealed class McpToolClientTests
{
    private const string Token = "eyJhbGciOiJub25lIn0.eyJzdWIiOiJ4In0.secret-signature";
    private static readonly Uri Endpoint = new("https://mcp.example.test/t1/mcp");

    private sealed class StubServer(Func<HttpRequestMessage, string, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return answer(request, body);
        }
    }

    private static HttpResponseMessage Sse(string json, string? session = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"event: message\ndata: {json}\n\n", Encoding.UTF8, "text/event-stream")
        };
        if (session != null)
        {
            response.Headers.Add("Mcp-Session-Id", session);
        }

        return response;
    }

    [Fact]
    public async Task CallTool_InitializesASession_SendsTheBearerToken_AndReturnsTheStructuredAnswer()
    {
        var server = new StubServer((_, body) => body.Contains("\"initialize\"")
            ? Sse("""{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-03-26"}}""", "session-7")
            : body.Contains("tools/call")
                ? Sse("""{"jsonrpc":"2.0","id":2,"result":{"content":[{"type":"text","text":"x"}],"structuredContent":{"isSuccess":true}}}""")
                : new HttpResponseMessage(HttpStatusCode.Accepted));
        using var client = new McpToolClient(server);

        var answer = await client.CallToolAsync(Endpoint, Token, "knowledge_list_platform_updates", new JsonObject { ["tenantId"] = "t1" },
            TestContext.Current.CancellationToken);

        Assert.True(answer!["isSuccess"]!.GetValue<bool>());
        Assert.Equal(3, server.Requests.Count);
        Assert.All(server.Requests, r => Assert.Equal($"Bearer {Token}", r.Request.Headers.Authorization!.ToString()));
        Assert.All(server.Requests.Skip(1), r => Assert.Equal("session-7", r.Request.Headers.GetValues("Mcp-Session-Id").Single()));
        Assert.Contains("\"name\":\"knowledge_list_platform_updates\"", server.Requests[2].Body);
    }

    [Fact]
    public async Task Http401_FailsWithTheLoginHint_AndWithoutTheToken()
    {
        var server = new StubServer((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent($"invalid token {Token}")
        });
        using var client = new McpToolClient(server);

        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            client.CallToolAsync(Endpoint, Token, "knowledge_adopt_platform_updates", new JsonObject(), TestContext.Current.CancellationToken));

        Assert.Contains("HTTP 401", ex.Message);
        Assert.Contains("LogIn", ex.Message);
        Assert.DoesNotContain(Token, ex.Message);
    }

    [Fact]
    public async Task ToolErrorResult_FailsWithoutTheToken()
    {
        var server = new StubServer((_, body) => body.Contains("\"initialize\"")
            ? Sse("""{"jsonrpc":"2.0","id":1,"result":{}}""")
            : body.Contains("tools/call")
                ? Sse("{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"isError\":true,\"content\":[{\"type\":\"text\",\"text\":\"boom " + Token + "\"}]}}")
                : new HttpResponseMessage(HttpStatusCode.Accepted));
        using var client = new McpToolClient(server);

        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            client.CallToolAsync(Endpoint, Token, "knowledge_adopt_platform_updates", new JsonObject(), TestContext.Current.CancellationToken));

        Assert.Contains("boom", ex.Message);
        Assert.DoesNotContain(Token, ex.Message);
    }

    [Fact]
    public void FindResponse_PicksTheAnswerToTheRequestFromAnSseStreamWithNotifications()
    {
        const string stream = "data: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\"}\n\n" +
                              "data: {\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"ok\":true}}\n\n";

        Assert.True(McpToolClient.FindResponse(stream, 2)!["result"]!["ok"]!.GetValue<bool>());
    }
}
