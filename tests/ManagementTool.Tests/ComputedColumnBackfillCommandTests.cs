using FakeItEasy;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
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

    private static IOptions<OctoToolOptions> Tenant() => Options.Create(new OctoToolOptions { TenantId = "acme" });

    [Fact]
    public async Task Retry_QueuesTheBackfill_WithOneCall()
    {
        var client = A.Fake<IStreamDataServicesClient>();
        A.CallTo(() => client.RetryComputedColumnBackfillAsync("acme", Archive, "ExportPower")).Returns(Job("j2", "Pending"));
        var logger = new RecordingLogger<RetryComputedColumnBackfillCommand>();
        var command = new RetryComputedColumnBackfillCommand(logger, Tenant(), client, A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(["-id", Archive, "-n", "ExportPower"]);

        await command.Execute();

        A.CallTo(() => client.RetryComputedColumnBackfillAsync("acme", Archive, "ExportPower")).MustHaveHappenedOnceExactly();
        A.CallTo(() => client.ListRecomputeJobsForArchiveAsync(A<string>._, A<string>._)).MustNotHaveHappened();
        Assert.Contains(logger.Entries, e => e.Message.Contains("queued as job j2"));
    }

    [Fact]
    public async Task Retry_WithWait_FollowsTheQueuedJobUntilCompleted()
    {
        var client = A.Fake<IStreamDataServicesClient>();
        A.CallTo(() => client.RetryComputedColumnBackfillAsync("acme", Archive, "ExportPower")).Returns(Job("j2", "Pending"));
        A.CallTo(() => client.ListRecomputeJobsForArchiveAsync("acme", Archive)).ReturnsNextFromSequence(
            new[] { Job("j2", "Running", 1000), Job("j1", "Failed") },
            new[] { Job("j2", "Completed", 748000), Job("j1", "Failed") });
        var logger = new RecordingLogger<RetryComputedColumnBackfillCommand>();
        var command = new RetryComputedColumnBackfillCommand(logger, Tenant(), client, A.Fake<IAuthenticationService>())
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
        A.CallTo(() => client.RetryComputedColumnBackfillAsync("acme", Archive, "ExportPower"))
            .Returns((RollupRecomputeJobInfoDto?)null);
        var command = new RetryComputedColumnBackfillCommand(new RecordingLogger<RetryComputedColumnBackfillCommand>(),
            Tenant(), client, A.Fake<IAuthenticationService>()) { PollInterval = TimeSpan.FromMilliseconds(1) };
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
        A.CallTo(() => client.RetryComputedColumnBackfillAsync("acme", Archive, "ExportPower")).Returns(Job("j2", "Pending"));
        A.CallTo(() => client.ListRecomputeJobsForArchiveAsync("acme", Archive)).ReturnsNextFromSequence(
            new[] { Job("j2", "Running", 1) }, new[] { Job("j2", "Running", 2) }, new[] { Job("j2", "Completed", 3) });
        var command = new RetryComputedColumnBackfillCommand(new RecordingLogger<RetryComputedColumnBackfillCommand>(),
            Tenant(), client, auth) { PollInterval = TimeSpan.FromMilliseconds(1) };
        command.CommandArgumentValue.ParseLayer(["-id", Archive, "-n", "ExportPower", "-w"]);

        await command.Execute();

        // Three polls, three refresh checks (PreValidate is not part of Execute).
        A.CallTo(() => auth.EnsureAuthenticated(A<Meshmakers.Octo.Sdk.ServiceClient.IServiceClientAccessToken>._))
            .MustHaveHappened(3, Times.Exactly);
    }

    [Fact]
    public async Task Add_WithWait_FailedJob_ExitsNonZero_AndNamesTheRetryCommand()
    {
        var client = A.Fake<IStreamDataServicesClient>();
        A.CallTo(() => client.ListRecomputeJobsForArchiveAsync("acme", Archive)).Returns(
            new[] { Job("j1", "Failed", error: "Backfill of computed column 'ExportPower' failed: XX000: Job killed") });
        var command = new AddComputedColumnCommand(new RecordingLogger<AddComputedColumnCommand>(), Tenant(), client,
            A.Fake<IAuthenticationService>()) { PollInterval = TimeSpan.FromMilliseconds(1) };
        command.CommandArgumentValue.ParseLayer(["-id", Archive, "-n", "ExportPower", "-f", "activepower * -1", "-r", "Double", "-w"]);

        var exception = await Assert.ThrowsAsync<ToolException>(() => command.Execute());

        Assert.Contains("Job killed", exception.Message);
        Assert.Contains("RetryComputedColumnBackfill", exception.Message);
        A.CallTo(() => client.AddComputedColumnAsync("acme", Archive, "ExportPower", "activepower * -1", "Double", true))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Add_WithoutWait_ReturnsAfterQueuing()
    {
        var client = A.Fake<IStreamDataServicesClient>();
        var command = new AddComputedColumnCommand(new RecordingLogger<AddComputedColumnCommand>(), Tenant(), client,
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
        var command = new UpdateComputedColumnFormulaCommand(logger, Tenant(), client, A.Fake<IAuthenticationService>())
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
