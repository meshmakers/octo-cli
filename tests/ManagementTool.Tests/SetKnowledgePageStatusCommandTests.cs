using System.Net;
using System.Text;
using System.Text.Json;
using FakeItEasy;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Knowledge;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#8565: <c>SetKnowledgePageStatus</c>, the human path to a knowledge page status (Verified is never set over MCP).
///     Needs a user token; reads the page, then writes the page status and a new KnowledgePageVersion (same content, the
///     user as author, the note) in one GraphQL mutation request.
/// </summary>
public sealed class SetKnowledgePageStatusCommandTests
{
    private const string Tenant = "ai-sandbox";
    private const string PageRtId = "67a1000000000000000a1c30";

    // The page of the golden hash test in octo-mcp-service (KnowledgeContentHashGoldenTests), as GraphQL returns it.
    private const string GoldenPageJson = """
        {"rtId":"67a1000000000000000a1c30","rtWellKnownName":"golden-page","title":"Golden page","kind":"HOW_TO",
         "summary":"Zebrafisch über alles.","verificationStatus":"VERIFIED","currentVersion":3,
         "sections":[{"heading":"Steps","sectionContent":"Do A.\nThen B.","sortOrder":1},
                     {"heading":"Intro","sectionContent":"Why.","sortOrder":0}],
         "citations":[{"sectionRef":"Intro","sourceQuote":"quote","sourceAnchor":"p.3","sourceRtId":"672000000000000000000101"},
                      {"sectionRef":"Steps","sourceQuote":"q2","sourceAnchor":null,"sourceRtId":null}],
         "hallucinationFlags":[{"claim":"c","reason":"r"}]}
        """;

    // Same value as octo-mcp-service KnowledgeContentHashGoldenTests.GoldenHash (status Verified).
    private const string GoldenHash = "c05d1afbca04a6e2fe064fda7056c0a10eb67657386299a21477e7d19e068b45";

    private readonly IAssetServicesClient _client = A.Fake<IAssetServicesClient>();
    private readonly ITenantTokenProvider _tokenProvider = A.Fake<ITenantTokenProvider>();
    private readonly List<(string Query, string? Variables)> _sent = [];

    private static string Jwt(object payload)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64("{\"alg\":\"none\"}")}.{B64(JsonSerializer.Serialize(payload))}.sig";
    }

    private static readonly string UserToken = Jwt(new { sub = "686a6215fd3e5972ff5693fd", tenant_id = Tenant });
    private static readonly string ClientToken = Jwt(new { client_id = "claude-agent", tenant_id = Tenant });

    private System.Text.Json.Nodes.JsonObject? _page;

    // AB#8968: the versions the tenant has (well-known name, versionNumber) and whether its model has BaseVersion.
    private readonly List<(string Name, int? Number)> _versions = [];
    private bool _modelHasBaseVersion = true;

    private (string Query, string? Variables) Write => _sent.Single(s => s.Query.StartsWith("mutation", StringComparison.Ordinal));

    /// <summary>
    ///     A stateful fake of the tenant: queries return the current page, the mutation applies the page update (like the
    ///     asset repository), and <paramref name="concurrentWrite" /> simulates a knowledge-tool write that lands with it.
    /// </summary>
    private void GivenPage(string pageJson = GoldenPageJson, string? writeError = null, bool concurrentWrite = false)
    {
        _page = System.Text.Json.Nodes.JsonNode.Parse(pageJson)!.AsObject();
        A.CallTo(() => _client.SendGraphQlAsync(A<string>._, A<string>._, A<string?>._, A<string?>._,
                A<CancellationToken>._))
            .ReturnsLazily((string _, string query, string? variables, string? _, CancellationToken _) =>
            {
                _sent.Add((query, variables));
                if (query.StartsWith("query knowledgePageVersions", StringComparison.Ordinal))
                {
                    return new GraphQlRawResponse(HistoryResponse(), HttpStatusCode.OK);
                }

                if (query.StartsWith("query", StringComparison.Ordinal))
                {
                    return new GraphQlRawResponse(
                        "{\"data\":{\"runtime\":{\"systemAiKnowledgePage\":{\"items\":[" + _page!.ToJsonString() + "]}}}}",
                        HttpStatusCode.OK);
                }

                if (writeError != null)
                {
                    return new GraphQlRawResponse(writeError, HttpStatusCode.OK);
                }

                var item = System.Text.Json.Nodes.JsonNode.Parse(variables!)!["page"]![0]!["item"]!;
                _page!["verificationStatus"] = item["verificationStatus"]!.GetValue<string>();
                _page["currentVersion"] = item["currentVersion"]!.GetValue<int>() + (concurrentWrite ? 1 : 0);
                if (concurrentWrite)
                {
                    _page["summary"] = "changed by an agent";
                }

                var version = System.Text.Json.Nodes.JsonNode.Parse(variables!)!["version"]![0]!;
                _versions.Add((version["rtWellKnownName"]!.GetValue<string>(), version["versionNumber"]!.GetValue<int>()));
                return new GraphQlRawResponse("""{"data":{"page":{},"version":{}}}""", HttpStatusCode.OK);
            });
    }

    /// <summary>
    ///     The answer of the history query: the stored versions, highest number first (the server sorts and filters by the
    ///     name pattern), plus the input fields of the version input type.
    /// </summary>
    private string HistoryResponse()
    {
        var items = new System.Text.Json.Nodes.JsonArray(_versions.OrderByDescending(v => v.Number ?? 0)
            .Select(v => (System.Text.Json.Nodes.JsonNode?)new System.Text.Json.Nodes.JsonObject
            {
                ["rtWellKnownName"] = v.Name,
                ["versionNumber"] = v.Number
            }).ToArray());
        var fields = new System.Text.Json.Nodes.JsonArray(
            new[] { "versionNumber", "restoredFromVersion" }.Concat(_modelHasBaseVersion ? ["baseVersion"] : [])
            .Select(n => (System.Text.Json.Nodes.JsonNode?)new System.Text.Json.Nodes.JsonObject { ["name"] = n })
            .ToArray());
        return new System.Text.Json.Nodes.JsonObject
        {
            ["data"] = new System.Text.Json.Nodes.JsonObject
            {
                ["runtime"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["systemAiKnowledgePageVersion"] = new System.Text.Json.Nodes.JsonObject { ["items"] = items }
                },
                ["versionInput"] = new System.Text.Json.Nodes.JsonObject { ["inputFields"] = fields }
            }
        }.ToJsonString();
    }

    /// <summary>The versions of the golden page up to its current version 3, as the knowledge tools wrote them.</summary>
    private void GivenHistory(params (string Name, int? Number)[] versions)
    {
        _versions.Clear();
        _versions.AddRange(versions);
    }

    private SetKnowledgePageStatusCommand Create(string? accessToken) =>
        new(new RecordingLogger<SetKnowledgePageStatusCommand>(), Options.Create(new OctoToolOptions { TenantId = Tenant }),
            _client, A.Fake<IAuthenticationService>(),
            Options.Create(new OctoToolAuthenticationOptions { AccessToken = accessToken }), _tokenProvider,
            new FixedTime(new DateTimeOffset(2026, 11, 8, 21, 0, 0, TimeSpan.Zero)));

    private async Task Run(string? accessToken, params string[] args)
    {
        var command = Create(accessToken);
        command.CommandArgumentValue.ParseLayer(args);
        await command.Execute();
    }

    [Fact]
    public void ContentHash_MatchesTheGoldenHashOfTheMcpService()
    {
        var page = KnowledgePageContent.FromGraphQl(JsonDocument.Parse(GoldenPageJson).RootElement);

        Assert.Equal(GoldenHash, page.HashWithStatus("VERIFIED"));
        Assert.NotEqual(GoldenHash, page.HashWithStatus("DRAFT"));
    }

    [Theory]
    [InlineData("HOW_TO", "HowTo")]
    [InlineData("IN_REVIEW", "InReview")]
    [InlineData("VERIFIED", "Verified")]
    public void EnumNames_ConvertBothWays(string constant, string pascal)
    {
        Assert.Equal(pascal, KnowledgePageContent.ToPascalCase(constant));
        Assert.Equal(constant, KnowledgePageContent.ToConstantCase(pascal));
    }

    [Fact]
    public async Task Verified_ByAUser_WritesThePageStatusAndANewVersionInOneMutation()
    {
        GivenPage(GoldenPageJson.Replace("\"VERIFIED\"", "\"IN_REVIEW\""));

        await Run(UserToken, "-s", "golden-page", "-st", "Verified", "-n", "checked against the runbook");

        Assert.Equal(4, _sent.Count); // read, version history, write, verifying re-read
        var (mutation, variablesJson) = Write;
        Assert.Contains("systemAiKnowledgePages", mutation);
        Assert.Contains("systemAiKnowledgePageVersions", mutation);

        using var variables = JsonDocument.Parse(variablesJson!);
        var page = variables.RootElement.GetProperty("page")[0];
        Assert.Equal(PageRtId, page.GetProperty("rtId").GetString());
        Assert.Equal("VERIFIED", page.GetProperty("item").GetProperty("verificationStatus").GetString());
        Assert.Equal(4, page.GetProperty("item").GetProperty("currentVersion").GetInt32());
        Assert.Equal(GoldenHash, page.GetProperty("item").GetProperty("contentHash").GetString());

        var version = variables.RootElement.GetProperty("version")[0];
        Assert.Equal("golden-page@4", version.GetProperty("rtWellKnownName").GetString());
        Assert.Equal(4, version.GetProperty("versionNumber").GetInt32());
        Assert.Equal("VERIFIED", version.GetProperty("verificationStatus").GetString());
        Assert.Equal("user:686a6215fd3e5972ff5693fd", version.GetProperty("author").GetString());
        Assert.Equal("checked against the runbook", version.GetProperty("changeNote").GetString());
        Assert.Equal("HOW_TO", version.GetProperty("kind").GetString());
        Assert.Equal("Golden page", version.GetProperty("title").GetString());
        Assert.Equal(2, version.GetProperty("sections").GetArrayLength());
        Assert.False(version.GetProperty("citations")[1].TryGetProperty("sourceAnchor", out _),
            "null record attributes are not written, like the knowledge tools");
        Assert.Equal(GoldenHash, version.GetProperty("contentHash").GetString());
        Assert.StartsWith("2026-11-08T21:00:00", version.GetProperty("authoredAt").GetString());
        var versionOf = version.GetProperty("versionOf")[0];
        Assert.Equal("CREATE", versionOf.GetProperty("modOption").GetString());
        Assert.Equal("System.Ai/KnowledgePage", versionOf.GetProperty("target").GetProperty("ckTypeId").GetString());
        Assert.Equal(PageRtId, versionOf.GetProperty("target").GetProperty("rtId").GetString());
    }

    [Theory]
    [InlineData("draft", "DRAFT")]
    [InlineData("InReview", "IN_REVIEW")]
    [InlineData("REJECTED", "REJECTED")]
    public async Task OtherStates_AreSetToo_WithADefaultNote(string argument, string expected)
    {
        GivenPage();

        await Run(UserToken, "-tid", "AI-Sandbox", "-s", "golden-page", "-st", argument);

        A.CallTo(() => _client.SendGraphQlAsync(Tenant, A<string>._, A<string?>._, A<string?>._, A<CancellationToken>._))
            .MustHaveHappened(4, Times.Exactly);
        using var variables = JsonDocument.Parse(Write.Variables!);
        var version = variables.RootElement.GetProperty("version")[0];
        Assert.Equal(expected, version.GetProperty("verificationStatus").GetString());
        Assert.StartsWith("status ", version.GetProperty("changeNote").GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClientCredentialsContext_OrNoToken_IsRefusedBeforeAnythingIsSent(bool clientToken)
    {
        GivenPage();

        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            Run(clientToken ? ClientToken : null, "-s", "golden-page", "-st", "Verified"));

        Assert.Contains("interactive user login", ex.Message);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task UnknownStatus_IsRefused()
    {
        GivenPage();

        var ex = await Assert.ThrowsAsync<ToolException>(() => Run(UserToken, "-s", "golden-page", "-st", "Approved"));

        Assert.Contains("Draft, InReview, Verified, Rejected", ex.Message);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task TooLongNote_IsRefused()
    {
        GivenPage();

        await Assert.ThrowsAsync<ToolException>(() =>
            Run(UserToken, "-s", "golden-page", "-st", "Verified", "-n", new string('x', 501)));

        Assert.Empty(_sent);
    }

    [Fact]
    public async Task UnknownPage_IsReportedAndNothingIsWritten()
    {
        A.CallTo(() => _client.SendGraphQlAsync(A<string>._, A<string>._, A<string?>._, A<string?>._,
                A<CancellationToken>._))
            .Returns(new GraphQlRawResponse("""{"data":{"runtime":{"systemAiKnowledgePage":{"items":[]}}}}""",
                HttpStatusCode.OK));

        var ex = await Assert.ThrowsAsync<ToolException>(() => Run(UserToken, "-s", "missing", "-st", "Verified"));

        Assert.Contains("not found", ex.Message);
        A.CallTo(() => _client.SendGraphQlAsync(A<string>._, A<string>.That.StartsWith("mutation"), A<string?>._,
            A<string?>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task GraphQlErrorOnWrite_IsReportedAsNothingWritten()
    {
        GivenPage(writeError: """{"errors":[{"message":"Forbidden for KnowledgePage"}],"data":null}""");

        var ex = await Assert.ThrowsAsync<ToolException>(() => Run(UserToken, "-s", "golden-page", "-st", "Verified"));

        Assert.Contains("nothing was written", ex.Message);
        Assert.Contains("Forbidden for KnowledgePage", ex.Message);
    }

    [Fact]
    public async Task ContextOfAnotherTenant_RecordsTheSubjectOfTheExchangedToken()
    {
        // A root-tenant context (octosystem) addressing ai-sandbox: the tenant sees the exchanged token, whose sub is the
        // user's id in ai-sandbox; the version must carry that author, as the knowledge tools record it.
        GivenPage();
        var rootToken = Jwt(new { sub = "root-user-id", tenant_id = "octosystem" });
        A.CallTo(() => _tokenProvider.GetAccessTokenAsync(Tenant, rootToken, A<CancellationToken>._))
            .Returns(UserToken);

        await Run(rootToken, "-s", "golden-page", "-st", "Rejected");

        using var variables = JsonDocument.Parse(Write.Variables!);
        Assert.Equal("user:686a6215fd3e5972ff5693fd",
            variables.RootElement.GetProperty("version")[0].GetProperty("author").GetString());
    }

    [Fact]
    public async Task ConcurrentContentWrite_IsReportedAsAConflict()
    {
        GivenPage(concurrentWrite: true);

        var ex = await Assert.ThrowsAsync<ToolException>(() => Run(UserToken, "-s", "golden-page", "-st", "Verified"));

        Assert.Contains("changed at the same time", ex.Message);
        Assert.Contains("read the page again", ex.Message);
    }

    /// <summary>
    ///     AB#8588: every request the command sends (read, write, re-read) is valid against the schema of a real asset
    ///     repository, with the variables it actually sends. The fake above accepts any document; the server does not.
    /// </summary>
    [Fact]
    public async Task EverySentRequest_IsValidAgainstTheAssetRepositorySchema()
    {
        GivenPage(GoldenPageJson.Replace("\"VERIFIED\"", "\"IN_REVIEW\""));

        await Run(UserToken, "-s", "golden-page", "-st", "Verified", "-n", "checked against the runbook");

        Assert.Equal(4, _sent.Count);
        Assert.Contains("baseVersion", Write.Variables);
        foreach (var (document, variables) in _sent)
        {
            var errors = await AssetRepositoryGraphQlSchema.ValidateAgainstSystemAi411Async(document, variables);
            Assert.True(errors.Count == 0,
                $"The asset repository refuses this request:\n{string.Join("\n", errors)}\n{document}\n{variables}");
        }
    }

    /// <summary>
    ///     AB#8968: on a tenant before System.Ai 4.11.0 (the snapshot itself: no <c>baseVersion</c>) every request is valid
    ///     too, because the command writes no BaseVersion there; and the snapshot does refuse a BaseVersion.
    /// </summary>
    [Fact]
    public async Task EverySentRequest_IsValidOnATenantWithoutBaseVersion()
    {
        _modelHasBaseVersion = false;
        GivenPage();

        await Run(UserToken, "-s", "golden-page", "-st", "Verified");

        Assert.Equal(4, _sent.Count);
        Assert.DoesNotContain("baseVersion", Write.Variables);
        foreach (var (document, variables) in _sent)
        {
            var errors = await AssetRepositoryGraphQlSchema.ValidateAsync(document, variables);
            Assert.True(errors.Count == 0,
                $"The asset repository refuses this request:\n{string.Join("\n", errors)}\n{document}\n{variables}");
        }

        var withBase = System.Text.Json.Nodes.JsonNode.Parse(Write.Variables!)!;
        withBase["version"]![0]!["baseVersion"] = 3;
        Assert.NotEmpty(await AssetRepositoryGraphQlSchema.ValidateAsync(Write.Query, withBase.ToJsonString()));
    }

    [Fact]
    public async Task UnadoptedBlueprintVersionAboveTheCurrentOne_IsNeverReused()
    {
        // The golden page is at @3; a blueprint update seeded @4 and @5 that are not adopted yet. CurrentVersion+1 would
        // write a second "golden-page@4".
        GivenPage();
        GivenHistory(("golden-page@1", 1), ("golden-page@2", 2), ("golden-page@3", 3), ("golden-page@4", 4),
            ("golden-page@5", 5));

        await Run(UserToken, "-s", "golden-page", "-st", "Verified");

        using var variables = JsonDocument.Parse(Write.Variables!);
        var item = variables.RootElement.GetProperty("page")[0].GetProperty("item");
        var version = variables.RootElement.GetProperty("version")[0];
        Assert.Equal(6, item.GetProperty("currentVersion").GetInt32());
        Assert.Equal("golden-page@6", version.GetProperty("rtWellKnownName").GetString());
        Assert.Equal(6, version.GetProperty("versionNumber").GetInt32());
        Assert.Equal(_versions.Count, _versions.Select(v => v.Name).Distinct().Count());
    }

    [Fact]
    public async Task NewVersion_RecordsTheReplacedVersionAsBaseVersion()
    {
        // The chain of AB#8780: the verify is written on @3, so the Blueprint versions up to @3 count as seen and the
        // platform-update rule reports none of them; a seeded @4 the person did not see stays pending.
        GivenPage();
        GivenHistory(("golden-page@1", 1), ("golden-page@2", 2), ("golden-page@3", 3), ("golden-page@4", 4));

        await Run(UserToken, "-s", "golden-page", "-st", "Verified");

        using var variables = JsonDocument.Parse(Write.Variables!);
        var version = variables.RootElement.GetProperty("version")[0];
        Assert.Equal(5, version.GetProperty("versionNumber").GetInt32());
        Assert.Equal(3, version.GetProperty("baseVersion").GetInt32());
        Assert.False(version.TryGetProperty("restoredFromVersion", out _), "a status change restores nothing");
    }

    [Fact]
    public async Task ConsecutiveStatusChanges_KeepTheChainLinked()
    {
        // InReview, then Verified: each version names the one before it, so the chain reaches back to @3.
        GivenPage();
        GivenHistory(("golden-page@1", 1), ("golden-page@2", 2), ("golden-page@3", 3));

        await Run(UserToken, "-s", "golden-page", "-st", "InReview");
        await Run(UserToken, "-s", "golden-page", "-st", "Verified");

        var writes = _sent.Where(s => s.Query.StartsWith("mutation", StringComparison.Ordinal))
            .Select(s => JsonDocument.Parse(s.Variables!).RootElement.GetProperty("version")[0]).ToList();
        Assert.Equal([4, 5], writes.Select(v => v.GetProperty("versionNumber").GetInt32()));
        Assert.Equal([3, 4], writes.Select(v => v.GetProperty("baseVersion").GetInt32()));
    }

    [Fact]
    public async Task TenantWithoutBaseVersion_StillTakesTheNextFreeNumber()
    {
        _modelHasBaseVersion = false;
        GivenPage();
        GivenHistory(("golden-page@3", 3), ("golden-page@7", 7));

        await Run(UserToken, "-s", "golden-page", "-st", "Rejected");

        using var variables = JsonDocument.Parse(Write.Variables!);
        var version = variables.RootElement.GetProperty("version")[0];
        Assert.Equal(8, version.GetProperty("versionNumber").GetInt32());
        Assert.False(version.TryGetProperty("baseVersion", out _));
    }

    [Fact]
    public void HighestVersion_CountsOnlyThisPage_AndFallsBackToTheName()
    {
        using var doc = JsonDocument.Parse("""
            {"runtime":{"systemAiKnowledgePageVersion":{"items":[
              {"rtWellKnownName":"golden-page@2","versionNumber":2},
              {"rtWellKnownName":"golden-page@9","versionNumber":null},
              {"rtWellKnownName":"golden-pagex@20","versionNumber":20},
              {"rtWellKnownName":"golden-page@x","versionNumber":30}]}}}
            """);

        Assert.Equal(9, SetKnowledgePageStatusCommand.HighestVersion(doc.RootElement, "golden-page"));
        Assert.Equal(0, SetKnowledgePageStatusCommand.HighestVersion(JsonDocument.Parse("{}").RootElement, "golden-page"));
    }

    [Fact]
    public void VersionNamePattern_EscapesTheSlug()
    {
        var pattern = SetKnowledgePageStatusCommand.VersionNamePattern("a.b+c");

        Assert.Equal(@"^a\.b\+c@[0-9]+$", pattern);
        Assert.Matches(pattern, "a.b+c@12");
        Assert.DoesNotMatch(pattern, "aXb+c@12");
        Assert.DoesNotMatch(pattern, "a.b+c@12-draft");
    }

    [Fact]
    public async Task HistoryReadError_IsReportedAndNothingIsWritten()
    {
        GivenPage();
        A.CallTo(() => _client.SendGraphQlAsync(A<string>._, A<string>.That.StartsWith("query knowledgePageVersions"),
                A<string?>._, A<string?>._, A<CancellationToken>._))
            .Returns(new GraphQlRawResponse("""{"errors":[{"message":"boom"}],"data":null}""", HttpStatusCode.OK));

        var ex = await Assert.ThrowsAsync<ToolException>(() => Run(UserToken, "-s", "golden-page", "-st", "Verified"));

        Assert.Contains("nothing was written", ex.Message);
        Assert.Contains("boom", ex.Message);
        A.CallTo(() => _client.SendGraphQlAsync(A<string>._, A<string>.That.StartsWith("mutation"), A<string?>._,
            A<string?>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task SchemaValidation_CatchesTheStringSlugOfAB8588()
    {
        // The read query as released in 2ae42f1: test-2 answered "Variable '$slug' of type 'String!' used in position
        // expecting type 'SimpleScalar'". Proves the validation above would have failed on it.
        var released = SetKnowledgePageStatusCommand.ReadQuery.Replace("$slug: SimpleScalar!", "$slug: String!");
        Assert.NotEqual(SetKnowledgePageStatusCommand.ReadQuery, released);

        var errors = await AssetRepositoryGraphQlSchema.ValidateAsync(released, """{"slug":"golden-page"}""");

        Assert.Contains(errors, e => e.Contains("$slug") && e.Contains("String!") && e.Contains("SimpleScalar"));
    }

    [Fact]
    public async Task SchemaValidation_ChecksTheVariableValuesToo()
    {
        // A wrong enum value in the variables (the status in PascalCase) is refused like on the server.
        GivenPage();
        await Run(UserToken, "-s", "golden-page", "-st", "Rejected");
        var (mutation, variables) = Write;
        Assert.Empty(await AssetRepositoryGraphQlSchema.ValidateAgainstSystemAi411Async(mutation, variables));

        var errors = await AssetRepositoryGraphQlSchema.ValidateAgainstSystemAi411Async(mutation,
            variables!.Replace("\"REJECTED\"", "\"Rejected\""));

        Assert.Contains(errors, e => e.Contains("$page") || e.Contains("$version"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-jwt")]
    [InlineData("a.%%%.c")]
    public void UserSubject_OfAnInvalidToken_IsNull(string? token) =>
        Assert.Null(SetKnowledgePageStatusCommand.UserSubject(token));
}

internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
