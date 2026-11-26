using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Ai.Agents;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     <c>CreateAgent</c>, <c>UpdateAgent</c>, <c>EnableAgent</c>, <c>DisableAgent</c>, <c>GetAgents</c> and
///     <c>GetAgentRuns</c> (AB#8455): the first five call the agent tools of the MCP service on the tenant route of the
///     context's <c>McpServiceUrl</c> with the user's own token, so the admin check stays in octo-mcp-service; a refusal
///     ends with the tool's code; the token never reaches output; <c>GetAgentRuns</c> says runs are not available yet.
/// </summary>
public sealed class AgentCommandsTests
{
    private const string Tenant = "agent-scratch";
    private const string McpUrl = "https://mcp.example.test/";

    private static readonly string UserToken = Jwt(new { sub = "686a6215fd3e5972ff5693fd", tenant_id = Tenant });

    private const string AgentAnswer = """
        {"isSuccess":true,"tenantId":"agent-scratch","assignedRoles":["Reader"],"agent":{
          "rtId":"6925a0c0e4b0a1b2c3d4e5f6","name":"Knowledge curator","instructions":"Keep the pages tidy.\nSecond line",
          "version":2,"enabled":true,"autonomyLevel":"ProposeOnly","runnerKind":"AgentSdk","model":null,
          "sponsorUserId":"686a6215fd3e5972ff5693fd","serviceAccountClientId":"agent-knowledge-curator",
          "serviceAccountClientEnabled":true,
          "mandate":{"rtId":"6925a0c0e4b0a1b2c3d4e5f7","toolGrants":[{"toolPattern":"knowledge_*","maxRisk":"Medium"}],
            "riskCeiling":"Medium","changeBudgetPerRun":0,"writeMode":"Propose","allowedCkTypes":["System.Ai/KnowledgePage"],
            "governedAgentCount":1}}}
        """;

    private const string ListAnswer = """
        {"isSuccess":true,"tenantId":"agent-scratch","totalCount":2,"agents":[
          {"rtId":"a1","name":"Knowledge curator","version":2,"enabled":true,"autonomyLevel":"ProposeOnly",
           "serviceAccountClientId":"agent-knowledge-curator","mandate":{"rtId":"m1","toolGrants":[{"toolPattern":"knowledge_*"}],
           "riskCeiling":"Medium","changeBudgetPerRun":0,"writeMode":"Propose"}},
          {"rtId":"a2","name":"Builder","version":1,"enabled":false,"autonomyLevel":"ActWithinBudget","mandate":null}
        ]}
        """;

    private readonly List<(Uri Endpoint, string Token, string Tool, JsonObject Arguments)> _calls = [];
    private readonly IConsoleService _console = A.Fake<IConsoleService>();
    private readonly IMcpToolClient _mcp = A.Fake<IMcpToolClient>();
    private readonly List<string> _printed = [];
    private readonly List<string> _logged = [];
    private string? _nextAnswer;

    public AgentCommandsTests()
    {
        A.CallTo(() => _console.WriteLine(A<string>._)).Invokes((string line) => _printed.Add(line));
        A.CallTo(() => _mcp.CallToolAsync(A<Uri>._, A<string>._, A<string>._, A<JsonObject>._, A<CancellationToken>._))
            .ReturnsLazily((Uri endpoint, string token, string tool, JsonObject arguments, CancellationToken _) =>
            {
                _calls.Add((endpoint, token, tool, arguments));
                return Task.FromResult<JsonNode?>(JsonNode.Parse(_nextAnswer ??
                                                                 (tool == AgentMcp.ListTool ? ListAnswer : AgentAnswer)));
            });
    }

    private static string Jwt(object payload)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64("{\"alg\":\"none\"}")}.{B64(JsonSerializer.Serialize(payload))}.sig";
    }

    private static IOptions<OctoToolOptions> Options(string? mcpUrl = McpUrl) =>
        Microsoft.Extensions.Options.Options.Create(new OctoToolOptions { TenantId = Tenant, McpServiceUrl = mcpUrl });

    private static IOptions<OctoToolAuthenticationOptions> Auth() =>
        Microsoft.Extensions.Options.Options.Create(new OctoToolAuthenticationOptions { AccessToken = UserToken });

    private async Task Run<T>(Func<ILogger<T>, T> create, params string[] args) where T : Command<OctoToolOptions>
    {
        var logger = new RecordingLogger<T>();
        var command = create(logger);
        command.CommandArgumentValue.ParseLayer(args);
        try
        {
            await command.Execute();
        }
        finally
        {
            _logged.AddRange(logger.Entries.Select(e => e.Message));
        }
    }

    private Task Create(IOptions<OctoToolOptions> options, params string[] args) =>
        Run<CreateAgentCommand>(l => new CreateAgentCommand(l, options, A.Fake<IAssetServicesClient>(),
            A.Fake<IAuthenticationService>(), Auth(), _mcp, _console, A.Fake<ITenantTokenProvider>()), args);

    private Task Update(params string[] args) =>
        Run<UpdateAgentCommand>(l => new UpdateAgentCommand(l, Options(), A.Fake<IAssetServicesClient>(),
            A.Fake<IAuthenticationService>(), Auth(), _mcp, _console, A.Fake<ITenantTokenProvider>()), args);

    private Task Enable(params string[] args) =>
        Run<EnableAgentCommand>(l => new EnableAgentCommand(l, Options(), A.Fake<IAssetServicesClient>(),
            A.Fake<IAuthenticationService>(), Auth(), _mcp, _console, A.Fake<ITenantTokenProvider>()), args);

    private Task Disable(params string[] args) =>
        Run<DisableAgentCommand>(l => new DisableAgentCommand(l, Options(), A.Fake<IAssetServicesClient>(),
            A.Fake<IAuthenticationService>(), Auth(), _mcp, _console, A.Fake<ITenantTokenProvider>()), args);

    private Task Get(params string[] args) =>
        Run<GetAgentsCommand>(l => new GetAgentsCommand(l, Options(), A.Fake<IAssetServicesClient>(),
            A.Fake<IAuthenticationService>(), Auth(), _mcp, _console, A.Fake<ITenantTokenProvider>()), args);

    [Fact]
    public async Task CreateAgent_CallsCreateAgentOnTheTenantRouteWithTheUsersToken_AndPassesEveryOption()
    {
        await Create(Options(), "-n", "Knowledge curator", "-i", "Keep the pages tidy.", "-m", "claude-sonnet-4-6",
            "-al", "ProposeOnly", "-g", "knowledge_*:Medium", "-g", "query_entities:Low:{\"ckTypeId\":[\"System.Ai/KnowledgePage\"]}",
            "-rc", "Medium", "-cb", "3", "-wm", "Propose", "-ct", "System.Ai/KnowledgePage", "-r", "Reader", "-d");

        var call = Assert.Single(_calls);
        Assert.Equal(AgentMcp.CreateTool, call.Tool);
        Assert.Equal(new Uri("https://mcp.example.test/agent-scratch/mcp"), call.Endpoint);
        Assert.Equal(UserToken, call.Token);
        var a = call.Arguments;
        Assert.Equal(Tenant, a["tenantId"]!.GetValue<string>());
        Assert.Equal("Knowledge curator", a["name"]!.GetValue<string>());
        Assert.Equal("Keep the pages tidy.", a["instructions"]!.GetValue<string>());
        Assert.False(a["enabled"]!.GetValue<bool>());
        Assert.Equal(3, a["changeBudgetPerRun"]!.GetValue<int>());
        Assert.Equal("Medium", a["riskCeiling"]!.GetValue<string>());
        var grants = a["toolGrants"]!.AsArray();
        Assert.Equal(2, grants.Count);
        Assert.Equal("knowledge_*", grants[0]!["toolPattern"]!.GetValue<string>());
        Assert.Equal("Medium", grants[0]!["maxRisk"]!.GetValue<string>());
        Assert.Equal("{\"ckTypeId\":[\"System.Ai/KnowledgePage\"]}", grants[1]!["argumentConstraints"]!.GetValue<string>());
        Assert.Equal("System.Ai/KnowledgePage", a["allowedCkTypes"]![0]!.GetValue<string>());
        Assert.Equal("Reader", a["roles"]![0]!.GetValue<string>());
        Assert.False(a.ContainsKey("clientSecret"));
        Assert.Contains(_printed, l => l.StartsWith("Agent created in tenant 'agent-scratch'", StringComparison.Ordinal));
        Assert.Contains(_printed, l => l.Contains("Knowledge curator") && l.Contains("ceiling Medium") && l.Contains("1 grant(s)"));
        Assert.Contains(_printed, l => l.Contains("Grant: knowledge_* up to Medium"));
        Assert.Contains(_printed, l => l.Contains("Roles assigned to the service-account client: Reader"));
    }

    [Fact]
    public async Task CreateAgent_WithoutOptionalValues_SendsOnlyNameInstructionsAndEnabled()
    {
        await Create(Options(), "-n", "Builder", "-i", "Build things.");

        var a = Assert.Single(_calls).Arguments;
        Assert.True(a["enabled"]!.GetValue<bool>());
        Assert.Equal(["name", "instructions", "enabled", "tenantId"], a.Select(p => p.Key).ToArray());
    }

    [Fact]
    public async Task CreateAgent_ReadsTheInstructionsFile()
    {
        var file = Path.Combine(Path.GetTempPath(), $"agent-instructions-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(file, "From a file.", TestContext.Current.CancellationToken);
        try
        {
            await Create(Options(), "-n", "Builder", "-if", file);
        }
        finally
        {
            File.Delete(file);
        }

        Assert.Equal("From a file.", Assert.Single(_calls).Arguments["instructions"]!.GetValue<string>());
    }

    [Theory]
    [InlineData]
    [InlineData("-i", "text", "-if", "file.md")]
    [InlineData("-i", "text", "-g", ":Medium")]
    [InlineData("-i", "text", "-g", "query_entities:Low:not-json")]
    public async Task CreateAgent_InvalidInput_IsRefusedBeforeAnyCall(params string[] extra)
    {
        await Assert.ThrowsAsync<ToolException>(() => Create(Options(), ["-n", "Builder", .. extra]));
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task NoMcpServiceUrlOnTheContext_IsRefusedWithTheWayToSetIt_BeforeAnyCall()
    {
        var ex = await Assert.ThrowsAsync<ToolException>(() => Create(Options(mcpUrl: null), "-n", "B", "-i", "x"));
        Assert.Contains("AddContext", ex.Message);
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task TheAgentCommandsOfferNoMcpServiceUrlOverride_TheTokenGoesOnlyToTheUrlOfTheContext()
    {
        const string evil = "https://evil.example.test/";
        await Create(Options(), "-n", "B", "-i", "x", "-msu", evil);
        await Update("-a", "x", "-cb", "1", "--mcpServiceUri", evil);
        await Enable("-a", "x", "-msu", evil);
        await Disable("-a", "x", "-msu", evil);
        await Get("-msu", evil);

        Assert.Equal(5, _calls.Count);
        Assert.All(_calls, c => Assert.Equal(new Uri("https://mcp.example.test/agent-scratch/mcp"), c.Endpoint));
    }

    [Fact]
    public async Task UpdateAgent_SendsOnlyTheGivenValues_AndClearFlagsSendEmptyLists()
    {
        await Update("-a", "Knowledge curator", "-cm", "-cb", "5", "-cg", "-cct", "-r", "Reader");

        var call = Assert.Single(_calls);
        Assert.Equal(AgentMcp.UpdateTool, call.Tool);
        var a = call.Arguments;
        Assert.Equal("Knowledge curator", a["agent"]!.GetValue<string>());
        Assert.Equal("", a["model"]!.GetValue<string>());
        Assert.Equal(5, a["changeBudgetPerRun"]!.GetValue<int>());
        Assert.Empty(a["toolGrants"]!.AsArray());
        Assert.Empty(a["allowedCkTypes"]!.AsArray());
        Assert.Equal("Reader", a["roles"]![0]!.GetValue<string>());
        Assert.False(a.ContainsKey("name"));
        Assert.False(a.ContainsKey("instructions"));
        Assert.Contains(_printed, l => l.StartsWith("Agent updated in tenant 'agent-scratch'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("-a", "x")]
    [InlineData("-a", "x", "-g", "knowledge_*", "-cg")]
    [InlineData("-a", "x", "-r", "Reader", "-cr")]
    [InlineData("-a", "x", "-m", "claude-sonnet-4-6", "-cm")]
    public async Task UpdateAgent_NothingOrContradictingOptions_IsRefusedBeforeAnyCall(params string[] args)
    {
        await Assert.ThrowsAsync<ToolException>(() => Update(args));
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task EnableAndDisableAgent_CallSetAgentEnabledWithTrueAndFalse()
    {
        await Enable("-a", "Knowledge curator");
        await Disable("-a", "Knowledge curator", "-tid", "Other-Tenant");

        Assert.Equal(2, _calls.Count);
        Assert.All(_calls, c => Assert.Equal(AgentMcp.SetEnabledTool, c.Tool));
        Assert.True(_calls[0].Arguments["enabled"]!.GetValue<bool>());
        Assert.False(_calls[1].Arguments["enabled"]!.GetValue<bool>());
        Assert.Equal("other-tenant", _calls[1].Arguments["tenantId"]!.GetValue<string>());
        Assert.Equal(new Uri("https://mcp.example.test/other-tenant/mcp"), _calls[1].Endpoint);
        Assert.Contains(_printed, l => l.StartsWith("Agent enabled in tenant 'agent-scratch'", StringComparison.Ordinal));
        Assert.Contains(_printed, l => l.StartsWith("Agent disabled in tenant 'other-tenant'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAgents_ListsEveryAgentInOneLine()
    {
        await Get();

        Assert.Equal(AgentMcp.ListTool, Assert.Single(_calls).Tool);
        Assert.Contains("Tenant 'agent-scratch': 2 agent(s).", _printed);
        Assert.Contains(_printed, l => l.Contains("Knowledge curator") && l.Contains("enabled") && l.Contains("1 grant(s)"));
        Assert.Contains(_printed, l => l.Contains("Builder") && l.Contains("disabled") && l.Contains("no mandate"));
    }

    [Fact]
    public async Task GetAgents_WithAgent_CallsGetAgent_AndShowsTheDetails()
    {
        await Get("-a", "Knowledge curator");

        var call = Assert.Single(_calls);
        Assert.Equal(AgentMcp.GetTool, call.Tool);
        Assert.Equal("Knowledge curator", call.Arguments["agent"]!.GetValue<string>());
        Assert.Contains(_printed, l => l.Contains("Service-account client: enabled"));
        Assert.Contains(_printed, l => l.Contains("Writable CK types: System.Ai/KnowledgePage"));
        Assert.Contains(_printed, l => l.Contains("Instructions: Keep the pages tidy.") && !l.Contains("Second line"));
    }

    [Fact]
    public async Task GetAgents_Json_PrintsTheAnswerOfTheTool()
    {
        await Get("--json");

        var printed = Assert.Single(_printed);
        Assert.Equal(2, JsonNode.Parse(printed)!["totalCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task ARefusalOfTheMcpService_FailsWithItsCodeAndMessage_ForANonAdministrator()
    {
        _nextAnswer = """{"isSuccess":false,"errorCode":"NotTenantAdministrator","errorMessage":"Only tenant administrators manage agents"}""";

        var ex = await Assert.ThrowsAsync<ToolException>(() => Disable("-a", "Knowledge curator"));
        Assert.Contains("NotTenantAdministrator", ex.Message);
        Assert.Contains("Only tenant administrators manage agents", ex.Message);
        Assert.Empty(_printed);
    }

    [Fact]
    public async Task APartialRefusalOfTheKillSwitch_DoesNotClaimThatNothingChanged()
    {
        // set_agent_enabled disabled the agent but could not disable its client (IdentityFailed).
        _nextAnswer = """{"isSuccess":false,"errorCode":"IdentityFailed","errorMessage":"The agent is disabled, but its client is still enabled"}""";

        var ex = await Assert.ThrowsAsync<ToolException>(() => Disable("-a", "Knowledge curator"));
        Assert.Contains("The agent is disabled, but its client is still enabled", ex.Message);
        Assert.DoesNotContain("Nothing was changed", ex.Message);
        Assert.Contains("partial change", ex.Message);
    }

    [Fact]
    public async Task ARefusalOfCreateAgent_SaysNothingWasChanged()
    {
        _nextAnswer = """{"isSuccess":false,"errorCode":"AgentExists","errorMessage":"An agent named 'Knowledge curator' exists already."}""";

        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            Create(Options(), "-n", "Knowledge curator", "-i", "Keep the pages tidy."));
        Assert.Contains("Nothing was changed by this command.", ex.Message);
    }

    [Fact]
    public async Task GetAgentRuns_SaysRunsAreNotAvailableYet_WithoutSignInOrCall()
    {
        var command = new GetAgentRunsCommand(new RecordingLogger<GetAgentRunsCommand>(), Options(),
            A.Fake<IAssetServicesClient>(), A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(["-a", "Knowledge curator"]);

        await command.PreValidate();
        var ex = await Assert.ThrowsAsync<ToolException>(() => command.Execute());
        Assert.Contains("not available yet", ex.Message);
        Assert.Contains("AB#8460", ex.Message);
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task TheTokenIsNeverLoggedOrPrinted_AtAnyLogLevel()
    {
        // RecordingLogger is enabled for every level, as with Trace in nlog.config.
        await Create(Options(), "-n", "Knowledge curator", "-i", "Keep the pages tidy.");
        await Update("-a", "Knowledge curator", "-cb", "1", "--json");
        await Enable("-a", "Knowledge curator");
        await Disable("-a", "Knowledge curator");
        await Get();
        await Get("-a", "Knowledge curator", "--json");

        var all = _logged.Concat(_printed).ToList();
        Assert.NotEmpty(_logged);
        Assert.DoesNotContain(all, line => line.Contains(UserToken, StringComparison.Ordinal));
        Assert.DoesNotContain(all, line => line.Contains("Bearer", StringComparison.OrdinalIgnoreCase));
    }
}
