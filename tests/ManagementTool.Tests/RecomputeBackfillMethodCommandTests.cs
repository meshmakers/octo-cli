using System.Text.Json;
using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static ManagementTool.Tests.StreamDataArchiveMethodCommandTests;

namespace ManagementTool.Tests;

/// <summary>
///     RecomputeArchive and BackfillRollup start <c>RollupArchive.Recompute-2</c> / <c>BackfillFromSource-2</c> and follow
///     the operation by its handle (<c>runtime.operations</c>): RecomputeArchive waits, as the blocking REST call did, and
///     prints the same job line; a job that ended Failed prints that line too and the command exits with 0 (decision of the
///     developer, as with REST). BackfillRollup prints the queued job at once and, with -w, the completed one. The answers are
///     the ones recorded from a stream-data tenant (octo-sdk <c>StreamDataMethodClientTests</c>).
/// </summary>
public sealed class RecomputeBackfillMethodCommandTests : IDisposable
{
    private const string RollupRtId = "6b0a88fefa75fad21127ffb3";

    private readonly GraphQlListener _service = new();
    private readonly IStreamDataServicesClient _streamData = A.Fake<IStreamDataServicesClient>();
    private readonly IAuthenticationService _auth = A.Fake<IAuthenticationService>();

    public void Dispose() => _service.Dispose();

    [Fact]
    public async Task Recompute_StartsRecompute2_WaitsForTheOperation_AndPrintsTheJobLineOfToday()
    {
        // Accepted answer and final operation of the same run (a recompute of the activated rollup).
        _service.RespondInOrder(Recorded("rollup-recompute2-accepted-activated"), Recorded("operation-recompute-succeeded"));
        var logger = new RecordingLogger<RecomputeArchiveCommand>();

        await Recompute(logger, "-id", RollupRtId, "-f", "2026-11-27T00:00:00Z", "-t", "2026-11-28T00:00:00Z").Execute();

        var line = Assert.Single(logger.Entries, e => e.Message.StartsWith("Recompute job ", StringComparison.Ordinal)).Message;
        Assert.Matches(@"^Recompute job 6b0a8911fa75fad21127ffbb: state=Completed, rows=0, windows=24, duration=209ms, lastProgress=2026-11-28T07:56:01\.\d+Z?, error=none$", line);
        Assert.Contains("systemStreamDataRollupArchives { recompute2(", Query(_service.Bodies[0]), StringComparison.Ordinal);
        Assert.Contains($"\"rtId\":\"{RollupRtId}\"", Variables(_service.Bodies[0]), StringComparison.Ordinal);
        Assert.Contains("\"from\":\"2026-11-27T00:00:00", Variables(_service.Bodies[0]), StringComparison.Ordinal);
        Assert.Contains("query CkOperationById", Query(_service.Bodies[1]), StringComparison.Ordinal);
        Assert.Contains("\"operationId\":\"55fd5a7137614c289ba808bc7b36b5e2\"", Variables(_service.Bodies[1]), StringComparison.Ordinal);
        A.CallTo(_streamData).Where(c => !c.Method.IsSpecialName).MustNotHaveHappened();
    }

    [Fact]
    public async Task Recompute_FailedJob_PrintsTheFailedJobLine_AndEndsWithoutAnError()
    {
        // The job of a rollup that is not activated ends Failed: STREAMDATA_RECOMPUTE_FAILED with jobRtId and reason. The
        // command reads the job from the archive's recompute jobs and prints it, as REST printed the Failed job (exit 0).
        _service.RespondInOrder(Recorded("rollup-recompute2-accepted"), Recorded("operation-recompute-failed"));
        A.CallTo(() => _streamData.ListRecomputeJobsForArchiveAsync("acme", RollupRtId)).Returns(
        [
            new RollupRecomputeJobInfoDto("6b0a8908fa75fad21127ffb6", "Failed", null, null, null, null, 7,
                "Archive is not activated (status Created) — activate it before recomputing.")
        ]);
        var logger = new RecordingLogger<RecomputeArchiveCommand>();

        await Recompute(logger, "-id", RollupRtId, "-f", "2026-11-27T00:00:00Z", "-t", "2026-11-28T00:00:00Z").Execute();

        Assert.Contains(logger.Entries, e => e.Message ==
            "Recompute job 6b0a8908fa75fad21127ffb6: state=Failed, rows=n/a, windows=n/a, duration=7ms, lastProgress=n/a, " +
            "error=Archive is not activated (status Created) — activate it before recomputing.");
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Recompute_FailedJobNoLongerListed_PrintsTheLineFromTheErrorParameters()
    {
        _service.RespondInOrder(Recorded("rollup-recompute2-accepted"), Recorded("operation-recompute-failed"));
        A.CallTo(() => _streamData.ListRecomputeJobsForArchiveAsync("acme", RollupRtId)).Returns([]);
        var logger = new RecordingLogger<RecomputeArchiveCommand>();

        await Recompute(logger, "-id", RollupRtId, "-f", "2026-11-27T00:00:00Z", "-t", "2026-11-28T00:00:00Z").Execute();

        Assert.Contains(logger.Entries, e => e.Message ==
            "Recompute job 6b0a8908fa75fad21127ffb6: state=Failed, rows=n/a, windows=n/a, duration=n/ams, lastProgress=n/a, " +
            "error=Archive is not activated (status Created) — activate it before recomputing.");
    }

    [Fact]
    public async Task Recompute_DeclaredErrorAtTheStart_FailsWithTheCode()
    {
        _service.RespondInOrder(MethodError("STREAMDATA_NOT_ENABLED", ("message", "Stream data is not enabled.")));

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() =>
            Recompute(new RecordingLogger<RecomputeArchiveCommand>(), "-id", RollupRtId, "-f", "2026-11-27T00:00:00Z",
                "-t", "2026-11-28T00:00:00Z").Execute());

        Assert.StartsWith("STREAMDATA_NOT_ENABLED: ", ex.Message, StringComparison.Ordinal);
        Assert.Single(_service.Bodies);
    }

    [Fact]
    public async Task Recompute_StillRunningAfterTheWait_EndsWithTheOperationId()
    {
        _service.RespondInOrder(Recorded("rollup-recompute2-accepted"), Running(null), Running(null), Running(null));
        var command = Recompute(new RecordingLogger<RecomputeArchiveCommand>(), "-id", RollupRtId, "-f",
            "2026-11-27T00:00:00Z", "-t", "2026-11-28T00:00:00Z");
        command.WaitTimeout = TimeSpan.FromSeconds(12);
        command.TimeProvider = new AdvancingTimeProvider();

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() => command.Execute());

        Assert.Contains("did not end within", ex.Message, StringComparison.Ordinal);
        Assert.Contains("operation 0b7f29f96d6348769cb8357648aba59d", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"ListRecomputeJobs -id {RollupRtId}", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Backfill_PrintsTheQueuedJob_AndReturnsWithoutWait()
    {
        _service.RespondInOrder(Recorded("rollup-backfill-from-source2-accepted"), Running("6b0a8911fa75fad21127ffc5"));
        var logger = new RecordingLogger<BackfillRollupCommand>();

        await Backfill(logger, "-id", RollupRtId).Execute();

        Assert.Contains(logger.Entries, e => e.Message ==
            $"Backfill of rollup '{RollupRtId}' queued as job 6b0a8911fa75fad21127ffc5 (state=Pending). It runs in the " +
            "background — poll with 'ListRecomputeJobs -id <rollupRtId>'.");
        Assert.Equal(2, _service.Bodies.Count);
        Assert.Contains("systemStreamDataRollupArchives { backfillFromSource2(", Query(_service.Bodies[0]), StringComparison.Ordinal);
        A.CallTo(_streamData).Where(c => !c.Method.IsSpecialName).MustNotHaveHappened();
    }

    [Fact]
    public async Task Backfill_WithWait_FollowsTheOperation_AndPrintsTheCompletedJob()
    {
        _service.RespondInOrder(Recorded("rollup-backfill-from-source2-accepted"), Running("6b0a8911fa75fad21127ffbb"),
            Recorded("operation-recompute-succeeded"));
        var logger = new RecordingLogger<BackfillRollupCommand>();

        await Backfill(logger, "-id", RollupRtId, "-w").Execute();

        var line = Assert.Single(logger.Entries, e => e.Message.StartsWith("Backfill job ", StringComparison.Ordinal)).Message;
        Assert.StartsWith("Backfill job 6b0a8911fa75fad21127ffbb: state=Completed, rows=0, windows=24, duration=209ms, lastProgress=",
            line, StringComparison.Ordinal);
        Assert.EndsWith(", error=none", line, StringComparison.Ordinal);
        A.CallTo(_streamData).Where(c => !c.Method.IsSpecialName).MustNotHaveHappened();
    }

    [Fact]
    public async Task Backfill_WithoutSourceData_IsTheNoOpOfToday()
    {
        _service.RespondInOrder(Recorded("rollup-backfill-from-source2-accepted"), Recorded("operation-backfill-succeeded"));
        var logger = new RecordingLogger<BackfillRollupCommand>();

        await Backfill(logger, "-id", RollupRtId, "-w").Execute();

        Assert.Contains(logger.Entries, e => e.Message ==
            $"Backfill of rollup '{RollupRtId}' was a no-op: the source archive holds no data.");
        Assert.DoesNotContain(logger.Entries, e => e.Message.StartsWith("Waiting", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Backfill_WithWait_AfterAnAssetRepositoryRestart_FollowsTheJobInTheJobList()
    {
        // The operation ends as METHOD_OUTCOME_UNKNOWN after a restart; the queued job goes on and stays listed.
        _service.RespondInOrder(Recorded("rollup-backfill-from-source2-accepted"), Running("6b0a8911fa75fad21127ffc5"),
            Failed("METHOD_OUTCOME_UNKNOWN", ("reason", "handler-lost")));
        A.CallTo(() => _streamData.ListRecomputeJobsForArchiveAsync("acme", RollupRtId)).Returns(
        [
            new RollupRecomputeJobInfoDto("6b0a8911fa75fad21127ffc5", "Completed", 100, 4, null, null, 50, null)
        ]);
        var logger = new RecordingLogger<BackfillRollupCommand>();

        await Backfill(logger, "-id", RollupRtId, "-w").Execute();

        Assert.Contains(logger.Entries, e => e.Message ==
            "Backfill job 6b0a8911fa75fad21127ffc5: state=Completed, rows=100, windows=4, duration=50ms, lastProgress=n/a, error=none");
    }

    [Fact]
    public async Task Backfill_WithWait_FailedJob_PrintsTheFailedJobLine()
    {
        _service.RespondInOrder(Recorded("rollup-backfill-from-source2-accepted"), Running("6b0a8908fa75fad21127ffb6"),
            Recorded("operation-recompute-failed"));
        A.CallTo(() => _streamData.ListRecomputeJobsForArchiveAsync("acme", RollupRtId)).Returns(
        [
            new RollupRecomputeJobInfoDto("6b0a8908fa75fad21127ffb6", "Failed", null, null, null, null, 7, "source gone")
        ]);
        var logger = new RecordingLogger<BackfillRollupCommand>();

        await Backfill(logger, "-id", RollupRtId, "-w").Execute();

        Assert.Contains(logger.Entries, e => e.Message ==
            "Backfill job 6b0a8908fa75fad21127ffb6: state=Failed, rows=n/a, windows=n/a, duration=7ms, lastProgress=n/a, error=source gone");
    }

    [Fact]
    public async Task Recompute_RefreshesTheTenantTokenBeforeEveryRead_AndTheStreamDataTokenBeforeTheJobList()
    {
        // A recompute outlives an access token: the tenant client is authenticated before the call and before every
        // operation read, the stream data client before the job list that follows the wait.
        _service.RespondInOrder(Recorded("rollup-recompute2-accepted"), Running(null), Running(null),
            Recorded("operation-recompute-failed"));
        A.CallTo(() => _streamData.ListRecomputeJobsForArchiveAsync("acme", RollupRtId)).Returns([]);
        var tenantClient = TenantClient();

        await Recompute(new RecordingLogger<RecomputeArchiveCommand>(), tenantClient, "-id", RollupRtId, "-f",
            "2026-11-27T00:00:00Z", "-t", "2026-11-28T00:00:00Z").Execute();

        // One for the call, one before each of the three operation reads.
        A.CallTo(() => _auth.EnsureAuthenticated(tenantClient.AccessToken)).MustHaveHappened(4, Times.Exactly);
        A.CallTo(() => _auth.EnsureAuthenticated(_streamData.AccessToken)).MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => _streamData.ListRecomputeJobsForArchiveAsync("acme", RollupRtId)).MustHaveHappened());
    }

    [Fact]
    public async Task Backfill_EndedBeforeTheFirstRead_PrintsTheFinalJobLine()
    {
        _service.RespondInOrder(Recorded("rollup-backfill-from-source2-accepted"), Recorded("operation-recompute-succeeded"));
        var logger = new RecordingLogger<BackfillRollupCommand>();

        await Backfill(logger, "-id", RollupRtId).Execute();

        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("runs in the background", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, e => e.Message.StartsWith(
            "Backfill job 6b0a8911fa75fad21127ffbb: state=Completed, rows=0, windows=24, duration=209ms", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Backfill_WithWait_NamingNoJobInTime_KeepsWaitingOnTheOperation()
    {
        _service.RespondInOrder([Recorded("rollup-backfill-from-source2-accepted"), .. Enumerable.Repeat(Running(null), 3),
            Recorded("operation-recompute-succeeded")]);
        var logger = new RecordingLogger<BackfillRollupCommand>();
        var command = Backfill(logger, "-id", RollupRtId, "-w");
        command.QueuedTimeout = TimeSpan.FromSeconds(12);

        await command.Execute();

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("named no job", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, e => e.Message.StartsWith("Backfill job 6b0a8911fa75fad21127ffbb: state=Completed",
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task Backfill_WithWait_WithoutAJobId_AfterARestart_SaysItMayStillRun()
    {
        _service.RespondInOrder([Recorded("rollup-backfill-from-source2-accepted"), .. Enumerable.Repeat(Running(null), 3),
            Failed("METHOD_OUTCOME_UNKNOWN", ("reason", "handler-lost"))]);
        var logger = new RecordingLogger<BackfillRollupCommand>();
        var command = Backfill(logger, "-id", RollupRtId, "-w");
        command.QueuedTimeout = TimeSpan.FromSeconds(12);

        await command.Execute();

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning &&
                                             e.Message.Contains("ended without an outcome (METHOD_OUTCOME_UNKNOWN", StringComparison.Ordinal) &&
                                             e.Message.Contains("ae9db76d1e4e47ea8beb3b81e7811ccb", StringComparison.Ordinal));
        A.CallTo(() => _streamData.ListRecomputeJobsForArchiveAsync(A<string>._, A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Backfill_WithWait_AfterARestart_RefreshesTheStreamDataTokenBeforeEveryPoll()
    {
        _service.RespondInOrder(Recorded("rollup-backfill-from-source2-accepted"), Running("6b0a8911fa75fad21127ffc5"),
            Failed("METHOD_OUTCOME_UNKNOWN", ("reason", "handler-lost")));
        A.CallTo(() => _streamData.ListRecomputeJobsForArchiveAsync("acme", RollupRtId)).ReturnsNextFromSequence(
            [new RollupRecomputeJobInfoDto("6b0a8911fa75fad21127ffc5", "Running", 1, 1, null, null, null, null)],
            [new RollupRecomputeJobInfoDto("6b0a8911fa75fad21127ffc5", "Completed", 100, 4, null, null, 50, null)]);

        await Backfill(new RecordingLogger<BackfillRollupCommand>(), "-id", RollupRtId, "-w").Execute();

        A.CallTo(() => _auth.EnsureAuthenticated(_streamData.AccessToken)).MustHaveHappened(2, Times.Exactly);
    }

    private RecomputeArchiveCommand Recompute(RecordingLogger<RecomputeArchiveCommand> logger, params string[] args) =>
        Recompute(logger, TenantClient(), args);

    private RecomputeArchiveCommand Recompute(RecordingLogger<RecomputeArchiveCommand> logger, ITenantClient tenantClient,
        params string[] args)
    {
        var command = new RecomputeArchiveCommand(logger, Tenant(), _streamData, tenantClient, _auth)
        {
            TimeProvider = new AdvancingTimeProvider()
        };
        command.CommandArgumentValue.ParseLayer(args);
        return command;
    }

    private BackfillRollupCommand Backfill(RecordingLogger<BackfillRollupCommand> logger, params string[] args)
    {
        var command = new BackfillRollupCommand(logger, Tenant(), _streamData, TenantClient(), _auth)
        {
            TimeProvider = new AdvancingTimeProvider(),
            PollInterval = TimeSpan.FromMilliseconds(1)
        };
        command.CommandArgumentValue.ParseLayer(args);
        return command;
    }

    private static IOptions<OctoToolOptions> Tenant() => Options.Create(new OctoToolOptions { TenantId = "acme" });

    private ITenantClient TenantClient()
    {
        var token = A.Fake<ITenantClientAccessToken>();
        A.CallTo(() => token.AccessToken).Returns(null);
        return new TenantClient(new TenantClientOptions { EndpointUri = _service.BaseUrl, TenantId = "acme" }, token);
    }

    /// <summary>The recorded succeeded recompute turned into a RUNNING operation; with a job id it is the QUEUED stage.</summary>
    private static string Running(string? jobRtId) => Operation(item =>
    {
        item["state"] = "RUNNING";
        item["result"] = null;
        item["completedAtUtc"] = null;
        item["progress"] = new JsonObject
        {
            ["percent"] = null,
            ["stageCode"] = jobRtId == null ? "RECOMPUTING" : "QUEUED",
            ["parameters"] = jobRtId == null
                ? new JsonArray()
                : new JsonArray(new JsonObject { ["name"] = "jobRtId", ["value"] = jobRtId })
        };
    });

    /// <summary>The recorded succeeded recompute turned into a FAILED operation with <paramref name="code" />.</summary>
    private static string Failed(string code, params (string Name, string Value)[] parameters) => Operation(item =>
    {
        item["state"] = "FAILED";
        item["result"] = null;
        item["error"] = new JsonObject
        {
            ["code"] = code,
            ["parameters"] = new JsonArray(parameters
                .Select(p => (JsonNode)new JsonObject { ["name"] = p.Name, ["value"] = p.Value }).ToArray()),
            ["details"] = new JsonArray()
        };
    });

    private static string Operation(Action<JsonObject> change)
    {
        var answer = JsonNode.Parse(Recorded("operation-recompute-succeeded"))!;
        change(answer["data"]!["runtime"]!["operations"]!["items"]![0]!.AsObject());
        return answer.ToJsonString(new JsonSerializerOptions());
    }

    /// <summary>Lets every delay elapse at once and moves the clock by it (the wait timeout reads the clock).</summary>
    private sealed class AdvancingTimeProvider : TimeProvider
    {
        private readonly Lock _sync = new();
        private DateTimeOffset _now = new(2026, 11, 28, 6, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            lock (_sync)
            {
                return _now;
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (_sync)
            {
                _now += dueTime;
            }

            ThreadPool.QueueUserWorkItem(_ => callback(state));
            return new NoTimer();
        }

        private sealed class NoTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
