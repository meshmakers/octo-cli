using FakeItEasy;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#6968 — ValidateRollups (read only) and RepairRollups (dry run by default, apply with confirmation) against a
///     fake stream data client.
/// </summary>
public sealed class RollupConsistencyCommandTests
{
    private const string RollupId = "6700000000000000000000a1";

    private static RollupConsistencyReportDto Ok() => new(true, DateTime.UtcNow, []);

    private static RollupConsistencyReportDto Broken() => new(false, DateTime.UtcNow,
    [
        new RollupConsistencyEntryDto(RollupId, false,
        [
            new RollupConsistencyFindingDto("OrphansAbovePointer", "rows above the pointer", null, null, 3, 120, null)
        ])
    ]);

    private static RollupRepairActionDto Action(string status, string kind = "DeleteOrphans", string? jobId = null) =>
        new(RollupId, kind, "step", status, null, null, 120, null, jobId);

    // ── ValidateRollups ────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_PrintsTheFindingsAndDoesNotFailByDefault()
    {
        var (command, client, console, _) = NewValidate();
        A.CallTo(() => client.ValidateRollupsAsync("acme", null)).Returns(Broken());
        command.CommandArgumentValue.ParseLayer([]);

        await command.Execute();

        Assert.Contains(console.Lines, l => l.Contains("OrphansAbovePointer") && l.Contains("generation 3"));
        Assert.Contains(console.Lines, l => l.Contains("1 inconsistent"));
    }

    [Fact]
    public async Task Validate_FailOnFindings_ThrowsWhenARollupIsInconsistent()
    {
        var (command, client, _, _) = NewValidate();
        A.CallTo(() => client.ValidateRollupsAsync("acme", null)).Returns(Broken());
        command.CommandArgumentValue.ParseLayer(["-ff"]);

        var exception = await Assert.ThrowsAsync<ToolException>(() => command.Execute());

        Assert.Contains("inconsistent", exception.Message);
    }

    [Fact]
    public async Task Validate_FailOnFindings_PassesWhenEverythingIsConsistent()
    {
        var (command, client, console, _) = NewValidate();
        A.CallTo(() => client.ValidateRollupsAsync("acme", null)).Returns(Ok());
        command.CommandArgumentValue.ParseLayer(["-ff"]);

        await command.Execute();

        Assert.Contains(console.Lines, l => l.Contains("all consistent"));
    }

    [Fact]
    public async Task Validate_WithIds_PassesTheTrimmedListToTheClient()
    {
        var (command, client, _, _) = NewValidate();
        IReadOnlyCollection<string>? passed = null;
        A.CallTo(() => client.ValidateRollupsAsync("acme", A<IReadOnlyCollection<string>?>._))
            .Invokes((string _, IReadOnlyCollection<string>? ids) => passed = ids)
            .Returns(Ok());
        command.CommandArgumentValue.ParseLayer(["-id", "r1, r2"]);

        await command.Execute();

        Assert.Equal(["r1", "r2"], passed);
    }

    [Fact]
    public async Task Validate_Json_PrintsTheReportAsJson()
    {
        var (command, client, console, _) = NewValidate();
        A.CallTo(() => client.ValidateRollupsAsync("acme", null)).Returns(Broken());
        command.CommandArgumentValue.ParseLayer(["-j"]);

        await command.Execute();

        Assert.Contains(console.Lines, l => l.Contains("\"RollupRtId\"") || l.Contains("\"rollupRtId\""));
    }

    [Fact]
    public async Task Validate_WithoutTenant_Throws()
    {
        var (command, _, _, _) = NewValidate(tenantId: null);
        command.CommandArgumentValue.ParseLayer([]);

        await Assert.ThrowsAsync<ToolException>(() => command.Execute());
    }

    // ── RepairRollups ──────────────────────────────────────────────────────

    [Fact]
    public async Task Repair_DefaultsToADryRun_WithoutConfirmationAndWithoutValidatingAgain()
    {
        var (command, client, console, confirmation, _) = NewRepair();
        A.CallTo(() => client.RepairRollupsAsync("acme", null, true))
            .Returns(new RollupRepairReportDto(true, true, Broken(), [Action("Planned")], null, null, []));
        command.CommandArgumentValue.ParseLayer([]);

        await command.Execute();

        A.CallTo(() => client.RepairRollupsAsync("acme", null, true)).MustHaveHappenedOnceExactly();
        A.CallTo(() => client.RepairRollupsAsync("acme", A<IReadOnlyCollection<string>?>._, false))
            .MustNotHaveHappened();
        Assert.Null(confirmation.LastMessage);
        Assert.Contains(console.Lines, l => l.Contains("DRY RUN"));
        Assert.Contains(console.Lines, l => l.Contains("DeleteOrphans") && l.Contains("Planned"));
        A.CallTo(() => client.ValidateRollupsAsync(A<string>._, A<IReadOnlyCollection<string>?>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Repair_ApplyDeclined_ThrowsAndChangesNothing()
    {
        var (command, client, _, _, _) = NewRepair(confirm: false);
        command.CommandArgumentValue.ParseLayer(["-a"]);

        await Assert.ThrowsAsync<ToolException>(() => command.Execute());

        A.CallTo(() => client.RepairRollupsAsync(A<string>._, A<IReadOnlyCollection<string>?>._, A<bool>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Repair_ApplyWithoutJobs_ValidatesAgainAndPassesWhenConsistent()
    {
        var (command, client, console, confirmation, _) = NewRepair();
        A.CallTo(() => client.RepairRollupsAsync("acme", null, false))
            .Returns(new RollupRepairReportDto(false, true, Broken(), [Action("Executed")], null, null, []));
        A.CallTo(() => client.ValidateRollupsAsync("acme", null)).Returns(Ok());
        command.CommandArgumentValue.ParseLayer(["-a"]);

        await command.Execute();

        Assert.NotNull(confirmation.LastMessage);
        Assert.Contains(console.Lines, l => l.Contains("after the repair"));
        Assert.Contains(console.Lines, l => l.Contains("all consistent"));
    }

    [Fact]
    public async Task Repair_ApplyWithoutJobs_StillInconsistent_Throws()
    {
        var (command, client, _, _, _) = NewRepair();
        A.CallTo(() => client.RepairRollupsAsync("acme", null, false))
            .Returns(new RollupRepairReportDto(false, true, Broken(), [Action("Executed")], null, null, []));
        A.CallTo(() => client.ValidateRollupsAsync("acme", null)).Returns(Broken());
        command.CommandArgumentValue.ParseLayer(["-a", "-y"]);

        var exception = await Assert.ThrowsAsync<ToolException>(() => command.Execute());

        Assert.Contains("still inconsistent", exception.Message);
    }

    [Fact]
    public async Task Repair_ApplyWithJobsAndNoWait_ReturnsAfterNamingTheJobs()
    {
        var (command, client, _, _, logger) = NewRepair();
        A.CallTo(() => client.RepairRollupsAsync("acme", null, false))
            .Returns(new RollupRepairReportDto(false, true, Broken(),
                [Action("Enqueued", "RecomputePointerRange", "job-1")], null, null, ["job-1"]));
        command.CommandArgumentValue.ParseLayer(["-a", "-y"]);

        await command.Execute();

        Assert.Contains(logger.Entries, e => e.Message.Contains("job-1") && e.Message.Contains("enqueued"));
        A.CallTo(() => client.ValidateRollupsAsync(A<string>._, A<IReadOnlyCollection<string>?>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Repair_ApplyWithWait_PollsTheJobAndValidatesAgain()
    {
        var (command, client, console, _, _) = NewRepair();
        A.CallTo(() => client.RepairRollupsAsync("acme", null, false))
            .Returns(new RollupRepairReportDto(false, true, Broken(),
                [Action("Enqueued", "RecomputePointerRange", "job-1")], null, null, ["job-1"]));
        A.CallTo(() => client.ListRecomputeJobsForArchiveAsync("acme", RollupId)).Returns(
            (IReadOnlyList<RollupRecomputeJobInfoDto>)
            [new RollupRecomputeJobInfoDto("job-1", "Completed", 10, 1, null, null, 5, null)]);
        A.CallTo(() => client.ValidateRollupsAsync("acme", null)).Returns(Ok());
        command.CommandArgumentValue.ParseLayer(["-a", "-y", "-w"]);

        await command.Execute();

        A.CallTo(() => client.ListRecomputeJobsForArchiveAsync("acme", RollupId)).MustHaveHappened();
        Assert.Contains(console.Lines, l => l.Contains("all consistent"));
    }

    [Fact]
    public async Task Repair_ApplyWithWait_FailedJob_Throws()
    {
        var (command, client, _, _, _) = NewRepair();
        A.CallTo(() => client.RepairRollupsAsync("acme", null, false))
            .Returns(new RollupRepairReportDto(false, true, Broken(),
                [Action("Enqueued", "RecomputePointerRange", "job-1")], null, null, ["job-1"]));
        A.CallTo(() => client.ListRecomputeJobsForArchiveAsync("acme", RollupId)).Returns(
            (IReadOnlyList<RollupRecomputeJobInfoDto>)
            [new RollupRecomputeJobInfoDto("job-1", "Failed", null, null, null, null, 5, "boom")]);
        command.CommandArgumentValue.ParseLayer(["-a", "-y", "-w"]);

        var exception = await Assert.ThrowsAsync<ToolException>(() => command.Execute());

        Assert.Contains("job-1", exception.Message);
        Assert.Contains("boom", exception.Message);
    }

    [Fact]
    public async Task Repair_FailedStep_ThrowsAfterPrintingTheReport()
    {
        var (command, client, console, _, _) = NewRepair();
        A.CallTo(() => client.RepairRollupsAsync("acme", null, false))
            .Returns(new RollupRepairReportDto(false, false, Broken(), [Action("Failed")], null,
                "DeleteOrphans on r1: timeout", []));
        command.CommandArgumentValue.ParseLayer(["-a", "-y"]);

        var exception = await Assert.ThrowsAsync<ToolException>(() => command.Execute());

        Assert.Contains("DeleteOrphans on r1: timeout", exception.Message);
        Assert.Contains(console.Lines, l => l.Contains("Failed step"));
    }

    [Fact]
    public async Task Repair_WithIds_PassesThemToTheClient()
    {
        var (command, client, _, _, _) = NewRepair();
        IReadOnlyCollection<string>? passed = null;
        A.CallTo(() => client.RepairRollupsAsync("acme", A<IReadOnlyCollection<string>?>._, true))
            .Invokes((string _, IReadOnlyCollection<string>? ids, bool _) => passed = ids)
            .Returns(new RollupRepairReportDto(true, true, Ok(), [], null, null, []));
        command.CommandArgumentValue.ParseLayer(["-id", "r1,r2"]);

        await command.Execute();

        Assert.Equal(["r1", "r2"], passed);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static (ValidateRollupsCommand Command, IStreamDataServicesClient Client, RecordingConsole Console,
        RecordingLogger<ValidateRollupsCommand> Logger) NewValidate(string? tenantId = "acme")
    {
        var client = A.Fake<IStreamDataServicesClient>();
        var console = new RecordingConsole();
        var logger = new RecordingLogger<ValidateRollupsCommand>();
        var command = new ValidateRollupsCommand(logger, Options.Create(new OctoToolOptions { TenantId = tenantId }),
            console, client, A.Fake<IAuthenticationService>());
        return (command, client, console, logger);
    }

    private static (RepairRollupsCommand Command, IStreamDataServicesClient Client, RecordingConsole Console,
        FakeConfirmation Confirmation, RecordingLogger<RepairRollupsCommand> Logger) NewRepair(bool confirm = true)
    {
        var client = A.Fake<IStreamDataServicesClient>();
        var console = new RecordingConsole();
        var confirmation = new FakeConfirmation(confirm);
        var logger = new RecordingLogger<RepairRollupsCommand>();
        var command = new RepairRollupsCommand(logger, Options.Create(new OctoToolOptions { TenantId = "acme" }),
            console, client, A.Fake<IAuthenticationService>(), confirmation)
        {
            PollInterval = TimeSpan.FromMilliseconds(1)
        };
        return (command, client, console, confirmation, logger);
    }

    private sealed class FakeConfirmation(bool answer) : IConfirmationService
    {
        public string? LastMessage { get; private set; }

        public bool Confirm(string message)
        {
            LastMessage = message;
            return answer;
        }
    }

    private sealed class RecordingConsole : IConsoleService
    {
        public List<string> Lines { get; } = [];

        public void WriteErrorLine(string text) => Lines.Add(text);
        public void WriteErrorLineRegardSpace(string text) => Lines.Add(text);

        public void WriteErrorColumnLine(string column1Text, int column1Length, string column2Text) =>
            Lines.Add($"{column1Text} {column2Text}");

        public void WriteLine(string text) => Lines.Add(text);
        public void WriteLineRegardSpace(string text) => Lines.Add(text);

        public void WriteColumnLine(string column1Text, int column1Length, string column2Text) =>
            Lines.Add($"{column1Text} {column2Text}");
    }
}
