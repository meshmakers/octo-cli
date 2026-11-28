using FakeItEasy;
using GraphQL;
using GraphQlDtos.DataTransferObjects.System.StreamData.v1;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#8002 — computed-column backfills run as background jobs: RetryComputedColumnBackfill re-queues a failed one with one
///     command, and -w on add / formula change / retry follows the job until it ends, against a fake stream data client.
/// </summary>
public sealed class ComputedColumnBackfillCommandTests
{
    private const string Archive = "6a0e000000000000000a0007";

    private static RollupRecomputeJobInfoDto Job(string id, string state, int? rows = null, string? error = null,
        string column = "ExportPower") =>
        new(id, state, rows, null, null, null, state is "Completed" or "Failed" ? 1234 : null, error)
        {
            ComputedColumnName = column,
        };

    private readonly ITenantClient _tenant = A.Fake<ITenantClient>();
    private readonly List<GraphQLRequest> _requests = [];

    public ComputedColumnBackfillCommandTests()
    {
        // The archive is a time-range archive; the backfill retry answers the queued job (the method's result record).
        A.CallTo(() => _tenant.SendQueryAsync<ArchiveMethodCall.ArchiveTypeDto>(A<GraphQLRequest>._)).Returns(
            new QlItemsContainer<ArchiveMethodCall.ArchiveTypeDto>
            {
                Items = [new ArchiveMethodCall.ArchiveTypeDto { RtId = Archive, CkTypeId = "System.StreamData/TimeRangeArchive" }]
            });
        MethodFakes.AnswerMethodCalls(_tenant, _requests, null, results:
        [
            new RtRecomputeJobInfoRecordDto { JobRtId = "j2", State = RtCkRecomputeJobStateEnum.Pending, ComputedColumnName = "ExportPower" }
        ]);
    }

    private static IOptions<OctoToolOptions> Tenant() => Options.Create(new OctoToolOptions { TenantId = "acme" });

    [Fact]
    public async Task Retry_QueuesTheBackfill_WithOneCall()
    {
        var client = A.Fake<IStreamDataServicesClient>();
        var logger = new RecordingLogger<RetryComputedColumnBackfillCommand>();
        var command = new RetryComputedColumnBackfillCommand(logger, Tenant(), client, _tenant, A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(["-id", Archive, "-n", "ExportPower"]);

        await command.Execute();

        var request = Assert.Single(_requests);
        Assert.Contains("retryComputedColumnBackfill(", request.Query, StringComparison.Ordinal);
        A.CallTo(() => client.ListRecomputeJobsForArchiveAsync(A<string>._, A<string>._)).MustNotHaveHappened();
        Assert.Contains(logger.Entries, e => e.Message.Contains("queued as job j2"));
    }

    [Fact]
    public async Task Retry_WithWait_FollowsTheQueuedJobUntilCompleted()
    {
        var client = A.Fake<IStreamDataServicesClient>();
        A.CallTo(() => client.ListRecomputeJobsForArchiveAsync("acme", Archive)).ReturnsNextFromSequence(
            new[] { Job("j2", "Running", 1000), Job("j1", "Failed") },
            new[] { Job("j2", "Completed", 748000), Job("j1", "Failed") });
        var logger = new RecordingLogger<RetryComputedColumnBackfillCommand>();
        var command = new RetryComputedColumnBackfillCommand(logger, Tenant(), client, _tenant, A.Fake<IAuthenticationService>())
        {
            PollInterval = TimeSpan.FromMilliseconds(1),
        };
        command.CommandArgumentValue.ParseLayer(["-id", Archive, "-n", "ExportPower", "-w"]);

        await command.Execute();

        Assert.Contains(logger.Entries, e => e.Message.Contains("is backfilled (748000 rows"));
    }

    [Fact]
    public async Task Retry_WithWait_WithoutAJobId_RefusesToGuessTheJob()
    {
        var client = A.Fake<IStreamDataServicesClient>();
        MethodFakes.AnswerMethodCalls(_tenant, _requests, null);
        var command = new RetryComputedColumnBackfillCommand(new RecordingLogger<RetryComputedColumnBackfillCommand>(),
            Tenant(), client, _tenant, A.Fake<IAuthenticationService>()) { PollInterval = TimeSpan.FromMilliseconds(1) };
        command.CommandArgumentValue.ParseLayer(["-id", Archive, "-n", "ExportPower", "-w"]);

        await Assert.ThrowsAsync<ToolException>(() => command.Execute());
        A.CallTo(() => client.ListRecomputeJobsForArchiveAsync(A<string>._, A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Wait_ReEnsuresAuthentication_BeforeEveryPoll()
    {
        // test-2, 2026-11-01: a 750k-row backfill outlived the access token; the wait stopped with a 401/403.
        var client = A.Fake<IStreamDataServicesClient>();
        var auth = A.Fake<IAuthenticationService>();
        A.CallTo(() => client.ListRecomputeJobsForArchiveAsync("acme", Archive)).ReturnsNextFromSequence(
            new[] { Job("j2", "Running", 1) }, new[] { Job("j2", "Running", 2) }, new[] { Job("j2", "Completed", 3) });
        var command = new RetryComputedColumnBackfillCommand(new RecordingLogger<RetryComputedColumnBackfillCommand>(),
            Tenant(), client, _tenant, auth) { PollInterval = TimeSpan.FromMilliseconds(1) };
        command.CommandArgumentValue.ParseLayer(["-id", Archive, "-n", "ExportPower", "-w"]);

        await command.Execute();

        // Three polls, three refresh checks (PreValidate is not part of Execute).
        A.CallTo(() => auth.EnsureAuthenticated(client.AccessToken)).MustHaveHappened(3, Times.Exactly);
    }

    [Fact]
    public async Task Wait_RidesOutARollout_503_AndFinishes()
    {
        var client = A.Fake<IStreamDataServicesClient>();
        var calls = 0;
        A.CallTo(() => client.ListRecomputeJobsForArchiveAsync("acme", Archive)).ReturnsLazily(() =>
            ++calls switch
            {
                1 => Task.FromResult<IReadOnlyList<RollupRecomputeJobInfoDto>>(new[] { Job("j2", "Running", 1) }),
                2 or 3 => throw new Meshmakers.Octo.Sdk.ServiceClient.ServiceClientResultException(
                    "503 Service Temporarily Unavailable", System.Net.HttpStatusCode.ServiceUnavailable),
                _ => Task.FromResult<IReadOnlyList<RollupRecomputeJobInfoDto>>(new[] { Job("j2", "Completed", 9) }),
            });
        var logger = new RecordingLogger<RetryComputedColumnBackfillCommand>();
        var command = new RetryComputedColumnBackfillCommand(logger, Tenant(), client, _tenant, A.Fake<IAuthenticationService>())
        {
            PollInterval = TimeSpan.FromMilliseconds(1),
        };
        command.CommandArgumentValue.ParseLayer(["-id", Archive, "-n", "ExportPower", "-w"]);

        await command.Execute();

        Assert.Contains(logger.Entries, e => e.Message.Contains("probably a rollout"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("is backfilled (9 rows"));
    }

    [Fact]
    public async Task Wait_NonTransientError_StillFails()
    {
        var client = A.Fake<IStreamDataServicesClient>();
        A.CallTo(() => client.ListRecomputeJobsForArchiveAsync("acme", Archive)).Throws(
            new Meshmakers.Octo.Sdk.ServiceClient.ServiceClientResultException("bad", System.Net.HttpStatusCode.BadRequest));
        var command = new RetryComputedColumnBackfillCommand(new RecordingLogger<RetryComputedColumnBackfillCommand>(),
            Tenant(), client, _tenant, A.Fake<IAuthenticationService>()) { PollInterval = TimeSpan.FromMilliseconds(1) };
        command.CommandArgumentValue.ParseLayer(["-id", Archive, "-n", "ExportPower", "-w"]);

        await Assert.ThrowsAsync<Meshmakers.Octo.Sdk.ServiceClient.ServiceClientResultException>(() => command.Execute());
    }

    [Fact]
    public async Task Add_WithWait_FailedJob_ExitsNonZero_AndNamesTheRetryCommand()
    {
        var client = A.Fake<IStreamDataServicesClient>();
        A.CallTo(() => client.ListRecomputeJobsForArchiveAsync("acme", Archive)).Returns(
            new[] { Job("j1", "Failed", error: "Backfill of computed column 'ExportPower' failed: XX000: Job killed") });
        var command = new AddComputedColumnCommand(new RecordingLogger<AddComputedColumnCommand>(), Tenant(), client, _tenant,
            A.Fake<IAuthenticationService>()) { PollInterval = TimeSpan.FromMilliseconds(1) };
        command.CommandArgumentValue.ParseLayer(["-id", Archive, "-n", "ExportPower", "-f", "activepower * -1", "-r", "Double", "-w"]);

        var exception = await Assert.ThrowsAsync<ToolException>(() => command.Execute());

        Assert.Contains("Job killed", exception.Message);
        Assert.Contains("RetryComputedColumnBackfill", exception.Message);
        var request = Assert.Single(_requests);
        Assert.Contains("addComputedColumn(", request.Query, StringComparison.Ordinal);
        A.CallTo(() => client.AddComputedColumnAsync(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<bool>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Add_WithoutWait_ReturnsAfterQueuing()
    {
        var client = A.Fake<IStreamDataServicesClient>();
        var command = new AddComputedColumnCommand(new RecordingLogger<AddComputedColumnCommand>(), Tenant(), client, _tenant,
            A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(["-id", Archive, "-n", "ExportPower", "-f", "activepower * -1", "-r", "Double"]);

        await command.Execute();

        A.CallTo(() => client.ListRecomputeJobsForArchiveAsync(A<string>._, A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task UpdateFormula_WithWait_WaitsForTheColumnsNewestJob()
    {
        var client = A.Fake<IStreamDataServicesClient>();
        A.CallTo(() => client.ListRecomputeJobsForArchiveAsync("acme", Archive)).Returns(
            new[] { Job("r9", "Completed", column: "Other"), Job("j3", "Completed", 10) });
        var logger = new RecordingLogger<UpdateComputedColumnFormulaCommand>();
        var command = new UpdateComputedColumnFormulaCommand(logger, Tenant(), client, _tenant, A.Fake<IAuthenticationService>())
        {
            PollInterval = TimeSpan.FromMilliseconds(1),
        };
        command.CommandArgumentValue.ParseLayer(["-id", Archive, "-n", "ExportPower", "-f", "activepower * -2", "-w"]);

        await command.Execute();

        Assert.Contains(logger.Entries, e => e.Message.Contains("is backfilled (10 rows"));
    }

    [Fact]
    public async Task ListRecomputeJobs_NamesTheComputedColumnOfABackfillJob()
    {
        var client = A.Fake<IStreamDataServicesClient>();
        A.CallTo(() => client.ListRecomputeJobsForArchiveAsync("acme", Archive)).Returns(new[] { Job("j1", "Running", 5) });
        var logger = new RecordingLogger<ListRecomputeJobsCommand>();
        var command = new ListRecomputeJobsCommand(logger, Tenant(), client, A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(["-id", Archive]);

        await command.Execute();

        Assert.Contains(logger.Entries, e => e.Message.Contains("backfill of computed column 'ExportPower'"));
    }
}
