using System.Text.Json;
using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     The stream-data archive commands (lifecycle, rollup freeze / unfreeze / rewind, recompute, backfill, archive and
///     computed columns) run the CK methods of <c>System.StreamData</c> through the method clients generated in GraphQlDtos
///     (<c>System.StreamData-[1.30,2.0)</c>): every command sends its method mutation addressed by runtime id, through the
///     client of the archive's concrete type, and never calls a REST write of <see cref="IStreamDataServicesClient" />.
///     Names, arguments and output stay as they were; a refused call ends with the code (exit -3), a service without the
///     methods with a message naming the version. The requests go to a loopback GraphQL endpoint; the method answers are the
///     ones recorded from a stream-data tenant (octo-sdk <c>StreamDataMethodClientTests</c>).
/// </summary>
public sealed class StreamDataArchiveMethodCommandTests : IDisposable
{
    private const string ArchiveRtId = "6b0a88eafa75fad21127ffa9";
    private const string RollupRtId = "6b0a88fefa75fad21127ffb3";
    private const string TimeRange = "System.StreamData/TimeRangeArchive";

    private readonly GraphQlListener _service = new();
    private readonly IStreamDataServicesClient _streamData = A.Fake<IStreamDataServicesClient>();
    private readonly IAuthenticationService _auth = A.Fake<IAuthenticationService>();
    private readonly IConfirmationService _confirmation = A.Fake<IConfirmationService>();

    /// <summary>
    ///     Command, arguments, the GraphQL path of its method, whether it first reads the archive's concrete type, and the
    ///     answer of the method (field name, result JSON).
    /// </summary>
    public static readonly Dictionary<string, (Type Command, string[] Args, string Field, bool ReadsType, string Result)> Cases = new()
    {
        ["ActivateArchive"] = (typeof(ActivateArchiveCommand), ["-id", ArchiveRtId],
            "systemStreamDataTimeRangeArchives { activate(", true, Transition("Activate")),
        ["DisableArchive"] = (typeof(DisableArchiveCommand), ["-id", ArchiveRtId],
            "systemStreamDataTimeRangeArchives { disable(", true, Transition("Disable")),
        ["EnableArchive"] = (typeof(EnableArchiveCommand), ["-id", ArchiveRtId],
            "systemStreamDataTimeRangeArchives { enable(", true, Transition("Enable")),
        ["RetryArchiveActivation"] = (typeof(RetryArchiveActivationCommand), ["-id", ArchiveRtId],
            "systemStreamDataTimeRangeArchives { retryActivation(", true, Transition("RetryActivation")),
        ["DeleteArchive"] = (typeof(DeleteArchiveCommand), ["-id", ArchiveRtId, "-y"],
            "systemStreamDataTimeRangeArchives { deleteArchive(", true, "true"),
        ["AddArchiveColumn"] = (typeof(AddArchiveColumnCommand), ["-id", ArchiveRtId, "-p", "WindowsProcessed"],
            "systemStreamDataTimeRangeArchives { addColumn(", true, Transition("AddArchiveColumn")),
        ["AddComputedColumn"] = (typeof(AddComputedColumnCommand), ["-id", ArchiveRtId, "-n", "Ratio", "-f", "a / b", "-r", "Double"],
            "systemStreamDataTimeRangeArchives { addComputedColumn(", true, Transition("AddComputedColumn")),
        ["RemoveComputedColumn"] = (typeof(RemoveComputedColumnCommand), ["-id", ArchiveRtId, "-n", "Ratio"],
            "systemStreamDataTimeRangeArchives { removeComputedColumn(", true, Transition("RemoveComputedColumn")),
        ["UpdateComputedColumnFormula"] = (typeof(UpdateComputedColumnFormulaCommand), ["-id", ArchiveRtId, "-n", "Ratio", "-f", "b / a"],
            "systemStreamDataTimeRangeArchives { updateComputedColumnFormula(", true, Transition("UpdateComputedColumnFormula")),
        ["RetryComputedColumnBackfill"] = (typeof(RetryComputedColumnBackfillCommand), ["-id", ArchiveRtId, "-n", "Ratio"],
            "systemStreamDataTimeRangeArchives { retryComputedColumnBackfill(", true,
            """{"jobRtId":"6b0a8911fa75fad21127ffc1","state":"Pending","computedColumnName":"Ratio"}"""),
        ["FreezeRollupArchive"] = (typeof(FreezeRollupArchiveCommand), ["-id", RollupRtId, "-u", "2026-11-27T12:00:00Z"],
            "systemStreamDataRollupArchives { freeze(", false, Transition("Freeze")),
        ["UnfreezeRollupArchive"] = (typeof(UnfreezeRollupArchiveCommand), ["-id", RollupRtId],
            "systemStreamDataRollupArchives { unfreeze(", false, Transition("Unfreeze")),
        ["RewindRollupWatermark"] = (typeof(RewindRollupWatermarkCommand), ["-id", RollupRtId, "-t", "2026-11-27T00:00:00Z"],
            "systemStreamDataRollupArchives { rewindWatermark(", false, Transition("RewindWatermark")),
    };

    public static TheoryData<string> CaseNames => new(Cases.Keys);

    public StreamDataArchiveMethodCommandTests()
    {
        A.CallTo(() => _confirmation.Confirm(A<string>._)).Returns(true);
        A.CallTo(() => _streamData.ServiceUri).Returns(new Uri("https://assets.example/acme/v1"));
    }

    public void Dispose() => _service.Dispose();

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task Command_RunsItsMethod_ByRuntimeId_AndCallsNoRestWrite(string name)
    {
        var (type, args, field, readsType, result) = Cases[name];
        Respond(readsType, Success(field, result));

        await Create(type, args).Execute();

        var mutation = _service.Bodies[^1];
        Assert.Contains(field, Query(mutation), StringComparison.Ordinal);
        Assert.Contains(readsType ? ArchiveRtId : RollupRtId, Variables(mutation), StringComparison.Ordinal);
        Assert.Equal(readsType ? 2 : 1, _service.Bodies.Count);
        if (readsType)
        {
            Assert.Contains("systemStreamDataArchive(rtId: $rtId)", Query(_service.Bodies[0]), StringComparison.Ordinal);
        }

        A.CallTo(_auth).Where(c => c.Method.Name == nameof(IAuthenticationService.EnsureAuthenticated)).MustHaveHappened();
        AssertNoRestWrite();
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task CommandDocuments_ValidateAgainstTheSchemaOfAStreamDataTenant(string name)
    {
        var (type, args, field, readsType, result) = Cases[name];
        Respond(readsType, Success(field, result));

        await Create(type, args).Execute();

        foreach (var body in _service.Bodies)
        {
            Assert.Empty(await AssetRepositoryGraphQlSchema.ValidateAgainstSystemStreamDataAsync(Query(body), Variables(body)));
        }
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task Command_RefusedByTheMethodGateway_FailsWithTheCode_AndCallsNoRestWrite(string name)
    {
        var (type, args, _, readsType, _) = Cases[name];
        Respond(readsType, MethodError("METHOD_FORBIDDEN", ("reason", "role-missing")));

        var ex = await Assert.ThrowsAsync<CkMethodCallException>(() => Create(type, args).Execute());

        Assert.Equal("METHOD_FORBIDDEN", ex.Code);
        Assert.Contains("reason=role-missing", CkMethodErrorText.Format(ex));
        AssertNoRestWrite();
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task ServerWithoutTheMethods_NamesTheNeededVersion_AndCallsNoRestWrite(string name)
    {
        var (type, args, _, readsType, _) = Cases[name];
        Respond(readsType, MethodError("METHOD_HANDLER_NOT_AVAILABLE"));

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() => Create(type, args).Execute());

        Assert.Contains("System.StreamData 1.30.0 or newer", ex.Message);
        Assert.Contains("does not fall back to the REST endpoint", ex.Message);
        AssertNoRestWrite();
    }

    [Fact]
    public async Task DeclaredError_FailsWithCodeMessageAndParameters()
    {
        // The answer recorded for Disable from status Created.
        Respond(true, Recorded("time-range-disable-invalid-transition"));

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() =>
            Create(typeof(DisableArchiveCommand), ["-id", "6b0a88f4fa75fad21127ffaf"]).Execute());

        Assert.StartsWith("STREAMDATA_INVALID_TRANSITION: The CK method call failed: STREAMDATA_INVALID_TRANSITION (", ex.Message,
            StringComparison.Ordinal);
        Assert.Contains("attemptedTransition=disable", ex.Message, StringComparison.Ordinal);
        Assert.Contains("fromStatus=Created", ex.Message, StringComparison.Ordinal);
        Assert.Contains("message=Cannot disable archive '6b0a88f4fa75fad21127ffaf' from status Created.", ex.Message, StringComparison.Ordinal);
        AssertNoRestWrite();
    }

    [Fact]
    public async Task ARefusedCall_ThroughTheRunner_PrintsCodeAndMessage_AndExitsWithMinusThree()
    {
        Respond(true, Recorded("time-range-disable-invalid-transition"));
        var command = Create(typeof(DisableArchiveCommand), ["-id", "6b0a88f4fa75fad21127ffaf"]);
        var parser = A.Fake<ICommandParser>();
        A.CallTo(() => parser.ParseAndValidateAsync(A<string?>._))
            .Returns(Task.Run(command.Execute, TestContext.Current.CancellationToken));
        var contexts = A.Fake<IContextManager>();
        A.CallTo(() => contexts.GetEffectiveContextName()).Returns("test");
        var logger = new RecordingLogger<Runner>();

        var exitCode = await new Runner(logger, parser, contexts, ContextSelection.None).DoActionAsync();

        Assert.Equal(-3, exitCode);
        var error = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains("STREAMDATA_INVALID_TRANSITION", error.Message);
        Assert.Contains("Cannot disable archive", error.Message);
    }

    [Fact]
    public async Task UnknownArchive_EndsBeforeAnyWrite()
    {
        _service.RespondInOrder(TypeAnswer(null));

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() =>
            Create(typeof(ActivateArchiveCommand), ["-id", ArchiveRtId]).Execute());

        Assert.Equal($"Archive '{ArchiveRtId}' not found.", ex.Message);
        Assert.Single(_service.Bodies);
    }

    [Fact]
    public async Task NoObjectId_EndsBeforeAnyCall()
    {
        var ex = await Assert.ThrowsAsync<ServiceClientException>(() =>
            Create(typeof(FreezeRollupArchiveCommand), ["-id", "rollup-1", "-u", "2026-11-27T12:00:00Z"]).Execute());

        Assert.Equal("RollupRtId 'rollup-1' is not a valid id.", ex.Message);
        Assert.Empty(_service.Bodies);
    }

    [Theory]
    [InlineData("System.StreamData/RawArchive", "systemStreamDataRawArchives { activate(")]
    [InlineData("System.StreamData/RawArchive-1", "systemStreamDataRawArchives { activate(")]
    [InlineData("System.StreamData/TimeRangeArchive-1", "systemStreamDataTimeRangeArchives { activate(")]
    [InlineData("System.StreamData/RollupArchive", "systemStreamDataRollupArchives { activate(")]
    [InlineData("System.StreamData/RollupArchive-2", "systemStreamDataRollupArchives { activate(")]
    public async Task Activate_UsesTheClientOfTheConcreteType_WhateverTheElementVersion(string ckTypeId, string field)
    {
        _service.RespondInOrder(TypeAnswer(ckTypeId), Success(field, Transition("Activate")));

        await Create(typeof(ActivateArchiveCommand), ["-id", ArchiveRtId]).Execute();

        Assert.Contains(field, Query(_service.Bodies[1]), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("System.StreamData/RecomputeJob")]
    [InlineData("System.Communication/Adapter")]
    public async Task Activate_OfANonArchiveType_EndsBeforeAnyWrite(string ckTypeId)
    {
        _service.RespondInOrder(TypeAnswer(ckTypeId));

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() =>
            Create(typeof(ActivateArchiveCommand), ["-id", ArchiveRtId]).Execute());

        Assert.Contains("which is no RawArchive, TimeRangeArchive or RollupArchive", ex.Message);
        Assert.Single(_service.Bodies);
    }

    [Theory]
    [InlineData("double", "DOUBLE")]
    [InlineData("Int64", "INT_64")]
    [InlineData("DateTime", "DATE_TIME")]
    public async Task AddComputedColumn_SendsTheResultType(string resultType, string wire)
    {
        Respond(true, Success("systemStreamDataTimeRangeArchives { addComputedColumn(", Transition("AddComputedColumn")));

        await Create(typeof(AddComputedColumnCommand), ["-id", ArchiveRtId, "-n", "Ratio", "-f", "a / b", "-r", resultType]).Execute();

        Assert.Contains($"\"resultType\":\"{wire}\"", Variables(_service.Bodies[^1]), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Decimal")]
    [InlineData("2")]
    public async Task AddComputedColumn_UnknownResultType_EndsBeforeAnyWrite(string resultType)
    {
        _service.RespondInOrder(TypeAnswer(TimeRange));

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() =>
            Create(typeof(AddComputedColumnCommand), ["-id", ArchiveRtId, "-n", "Ratio", "-f", "a", "-r", resultType]).Execute());

        Assert.Equal($"Result type '{resultType}' is not valid. Use one of: Boolean, Int, Int64, Double, DateTime.", ex.Message);
        Assert.DoesNotContain(_service.Bodies, b => Query(b).StartsWith("mutation", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RetryComputedColumnBackfill_PrintsTheQueuedJob()
    {
        Respond(true, Success("systemStreamDataTimeRangeArchives { retryComputedColumnBackfill(",
            Cases["RetryComputedColumnBackfill"].Result));
        var logger = new RecordingLogger<RetryComputedColumnBackfillCommand>();

        await Create(typeof(RetryComputedColumnBackfillCommand), ["-id", ArchiveRtId, "-n", "Ratio"], logger).Execute();

        Assert.Contains(logger.Entries,
            e => e.Message == "Backfill of computed column 'Ratio' queued as job 6b0a8911fa75fad21127ffc1 (Pending).");
    }

    [Theory]
    [InlineData("System.StreamData/RawArchive", "Raw")]
    [InlineData("System.StreamData/TimeRangeArchive-1", "TimeRange")]
    [InlineData("System.StreamData/RollupArchive-12", "Rollup")]
    [InlineData("System.StreamData/Archive", null)]
    [InlineData("System.StreamData/RollupArchive-", null)]
    [InlineData("Other.StreamData/RawArchive", null)]
    public void ParseArchiveKind(string ckTypeId, string? expected) =>
        Assert.Equal(expected, ArchiveMethodCall.ParseArchiveKind(ckTypeId)?.ToString());

    private void Respond(bool readsType, string methodAnswer)
    {
        if (readsType)
        {
            _service.RespondInOrder(TypeAnswer(TimeRange), methodAnswer);
        }
        else
        {
            _service.RespondInOrder(methodAnswer);
        }
    }

    private ServiceClientOctoCommand<IStreamDataServicesClient> Create(Type type, string[] args, ILogger? logger = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(RecordingLogger<>));
        services.AddSingleton(Options.Create(new OctoToolOptions { TenantId = "acme" }));
        services.AddSingleton(_streamData);
        services.AddSingleton(TenantClient());
        services.AddSingleton(_auth);
        services.AddSingleton(_confirmation);
        var provider = services.BuildServiceProvider();
        var command = (ServiceClientOctoCommand<IStreamDataServicesClient>)(logger == null
            ? ActivatorUtilities.CreateInstance(provider, type)
            : ActivatorUtilities.CreateInstance(provider, type, logger));
        command.CommandArgumentValue.ParseLayer(args);
        return command;
    }

    private ITenantClient TenantClient()
    {
        var token = A.Fake<ITenantClientAccessToken>();
        A.CallTo(() => token.AccessToken).Returns(null);
        return new TenantClient(new TenantClientOptions { EndpointUri = _service.BaseUrl, TenantId = "acme" }, token);
    }

    private void AssertNoRestWrite() =>
        A.CallTo(_streamData).Where(c => !c.Method.IsSpecialName).MustNotHaveHappened();

    private static string Transition(string transition) =>
        $$"""{"archiveRtId":"{{ArchiveRtId}}","status":"ACTIVATED","transition":"{{transition}}"}""";

    /// <summary>A success answer of the method gateway for the mutation path <paramref name="field" /> (e.g. <c>systemStreamDataRollupArchives { freeze(</c>).</summary>
    internal static string Success(string field, string resultJson)
    {
        var parts = field.Split(" { ");
        var method = parts[1].TrimEnd('(');
        var answer = new JsonObject
        {
            ["data"] = new JsonObject
            {
                ["runtime"] = new JsonObject
                {
                    [parts[0]] = new JsonObject
                    {
                        [method] = new JsonObject { ["invocationId"] = "inv-1", ["result"] = JsonNode.Parse(resultJson) }
                    }
                }
            }
        };
        return answer.ToJsonString();
    }

    internal static string TypeAnswer(string? ckTypeId)
    {
        var items = new JsonArray();
        if (ckTypeId != null)
        {
            items.Add(new JsonObject { ["rtId"] = ArchiveRtId, ["ckTypeId"] = ckTypeId });
        }

        return new JsonObject
        {
            ["data"] = new JsonObject
            {
                ["runtime"] = new JsonObject
                {
                    ["systemStreamDataArchive"] = new JsonObject { ["totalCount"] = items.Count, ["items"] = items }
                }
            }
        }.ToJsonString();
    }

    internal static string MethodError(string code, params (string Name, string Value)[] parameters) =>
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
            data = (object?)null
        });

    internal static string Recorded(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"assetrepo-streamdata-answer-{name}.json"));

    internal static string Query(string body)
    {
        using var request = JsonDocument.Parse(body);
        return request.RootElement.GetProperty("query").GetString()!;
    }

    internal static string Variables(string body)
    {
        using var request = JsonDocument.Parse(body);
        return request.RootElement.TryGetProperty("variables", out var variables) ? variables.GetRawText() : "{}";
    }
}
