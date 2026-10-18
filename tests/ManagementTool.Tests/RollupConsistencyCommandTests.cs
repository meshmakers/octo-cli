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

    private static RollupRepairActionDto Action(string status, string kind = "DeleteOrphans") =>
        new(RollupId, kind, "step", status, null, null, 3, null);

    private static RollupRepairRequestDto Request(string state, RollupRepairReportDto? report = null) =>
        new("req-1", state, DateTime.UtcNow, report is null ? null : DateTime.UtcNow,
            [Action("Planned", "RecomputePointerRange")], report);

    private static RollupRepairReportDto FinalReport(bool ok = true, string? failedStep = null) =>
        new(false, ok, Broken(), [Action("Done")], ok ? Ok() : Broken(), failedStep);

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
    public async Task Repair_DefaultsToADryRun_WithoutConfirmationAndWithoutEnqueueing()
    {
        var (command, client, console, confirmation, _) = NewRepair();
        A.CallTo(() => client.PlanRollupRepairAsync("acme", null))
            .Returns(new RollupRepairReportDto(true, true, Broken(), [Action("Planned")], null, null));
        command.CommandArgumentValue.ParseLayer([]);

        await command.Execute();

        A.CallTo(() => client.PlanRollupRepairAsync("acme", null)).MustHaveHappenedOnceExactly();
        A.CallTo(() => client.EnqueueRollupRepairAsync(A<string>._, A<IReadOnlyCollection<string>?>._))
            .MustNotHaveHappened();
        Assert.Null(confirmation.LastMessage);
        Assert.Contains(console.Lines, l => l.Contains("DRY RUN"));
        Assert.Contains(console.Lines, l => l.Contains("DeleteOrphans") && l.Contains("Planned"));
    }

    [Fact]
    public async Task Repair_ApplyDeclined_ThrowsAndEnqueuesNothing()
    {
        var (command, client, _, _, _) = NewRepair(confirm: false);
        command.CommandArgumentValue.ParseLayer(["-a"]);

        await Assert.ThrowsAsync<ToolException>(() => command.Execute());

        A.CallTo(() => client.EnqueueRollupRepairAsync(A<string>._, A<IReadOnlyCollection<string>?>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Repair_ApplyWithoutWait_PrintsTheRequestIdAndReturns()
    {
        var (command, client, console, confirmation, logger) = NewRepair();
        A.CallTo(() => client.EnqueueRollupRepairAsync("acme", null)).Returns(Request("Pending"));
        command.CommandArgumentValue.ParseLayer(["-a"]);

        await command.Execute();

        Assert.NotNull(confirmation.LastMessage);
        Assert.Contains(console.Lines, l => l.Contains("req-1") && l.Contains("Pending"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("GetRollupRepairStatus -rid req-1"));
        A.CallTo(() => client.GetRollupRepairRequestAsync(A<string>._, A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Repair_ApplyWithWait_PollsUntilDoneAndPrintsTheFinalReport()
    {
        var (command, client, console, _, _) = NewRepair();
        A.CallTo(() => client.EnqueueRollupRepairAsync("acme", null)).Returns(Request("Pending"));
        A.CallTo(() => client.GetRollupRepairRequestAsync("acme", "req-1"))
            .ReturnsNextFromSequence(Request("Running"), Request("Done", FinalReport()));
        command.CommandArgumentValue.ParseLayer(["-a", "-y", "-w"]);

        await command.Execute();

        A.CallTo(() => client.GetRollupRepairRequestAsync("acme", "req-1")).MustHaveHappenedTwiceExactly();
        Assert.Contains(console.Lines, l => l.Contains("after the repair"));
        Assert.Contains(console.Lines, l => l.Contains("all consistent"));
    }

    [Fact]
    public async Task Repair_ApplyWithWait_FailedRequest_ThrowsNamingTheFailedStep()
    {
        var (command, client, console, _, _) = NewRepair();
        A.CallTo(() => client.EnqueueRollupRepairAsync("acme", null)).Returns(Request("Pending"));
        A.CallTo(() => client.GetRollupRepairRequestAsync("acme", "req-1"))
            .Returns(Request("Failed", FinalReport(false, "RecomputePointerRange on r1: timeout")));
        command.CommandArgumentValue.ParseLayer(["-a", "-y", "-w"]);

        var exception = await Assert.ThrowsAsync<ToolException>(() => command.Execute());

        Assert.Contains("RecomputePointerRange on r1: timeout", exception.Message);
        Assert.Contains(console.Lines, l => l.Contains("Failed step"));
    }

    [Fact]
    public async Task Repair_ApplyWithWait_DoneButStillInconsistent_Throws()
    {
        var (command, client, _, _, _) = NewRepair();
        A.CallTo(() => client.EnqueueRollupRepairAsync("acme", null)).Returns(Request("Pending"));
        A.CallTo(() => client.GetRollupRepairRequestAsync("acme", "req-1"))
            .Returns(Request("Done", FinalReport(false)));
        command.CommandArgumentValue.ParseLayer(["-a", "-y", "-w"]);

        await Assert.ThrowsAsync<ToolException>(() => command.Execute());
    }

    [Fact]
    public async Task Repair_WithIds_PassesThemToTheClient()
    {
        var (command, client, _, _, _) = NewRepair();
        IReadOnlyCollection<string>? passed = null;
        A.CallTo(() => client.PlanRollupRepairAsync("acme", A<IReadOnlyCollection<string>?>._))
            .Invokes((string _, IReadOnlyCollection<string>? ids) => passed = ids)
            .Returns(new RollupRepairReportDto(true, true, Ok(), [], null, null));
        command.CommandArgumentValue.ParseLayer(["-id", "r1,r2"]);

        await command.Execute();

        Assert.Equal(["r1", "r2"], passed);
    }

    // ── GetRollupRepairStatus ──────────────────────────────────────────────

    [Fact]
    public async Task Status_PrintsTheStateAndTheFinalReport()
    {
        var (command, client, console) = NewStatus();
        A.CallTo(() => client.GetRollupRepairRequestAsync("acme", "req-1")).Returns(Request("Done", FinalReport()));
        command.CommandArgumentValue.ParseLayer(["-rid", "req-1"]);

        await command.Execute();

        Assert.Contains(console.Lines, l => l.Contains("req-1") && l.Contains("Done"));
        Assert.Contains(console.Lines, l => l.Contains("all consistent"));
    }

    [Fact]
    public async Task Status_FailOnFailed_ThrowsForAFailedRequestOnly()
    {
        var (command, client, _) = NewStatus();
        A.CallTo(() => client.GetRollupRepairRequestAsync("acme", "req-1"))
            .Returns(Request("Failed", FinalReport(false, "step")));
        command.CommandArgumentValue.ParseLayer(["-rid", "req-1", "-ff"]);

        await Assert.ThrowsAsync<ToolException>(() => command.Execute());

        var (running, runningClient, _) = NewStatus();
        A.CallTo(() => runningClient.GetRollupRepairRequestAsync("acme", "req-1")).Returns(Request("Running"));
        running.CommandArgumentValue.ParseLayer(["-rid", "req-1", "-ff"]);

        await running.Execute();
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

    private static (GetRollupRepairStatusCommand Command, IStreamDataServicesClient Client, RecordingConsole Console)
        NewStatus()
    {
        var client = A.Fake<IStreamDataServicesClient>();
        var console = new RecordingConsole();
        var command = new GetRollupRepairStatusCommand(new RecordingLogger<GetRollupRepairStatusCommand>(),
            Options.Create(new OctoToolOptions { TenantId = "acme" }), console, client,
            A.Fake<IAuthenticationService>());
        return (command, client, console);
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
