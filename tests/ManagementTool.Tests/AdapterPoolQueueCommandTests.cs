using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#4924 §10 — the CLI half of the adapter pool queue, one of the three surfaces concept §5
///     asks for. What is pinned here is what the design decided and what a well-meaning change would
///     otherwise undo:
///     <list type="bullet">
///         <item>the queue prints the tenant-local position <b>and</b> the tenants ahead in the
///             rotation, never a single global rank (§9.2);</item>
///         <item>a leased entry shows its member and is called out as <b>not</b> cancellable through
///             the queue verb;</item>
///         <item>a 409 is reported as "already leased, interrupting is a different operation" rather
///             than as a generic failure (§5 "Cancellation");</item>
///         <item>an empty queue reads as an idle pool, not as an error.</item>
///     </list>
/// </summary>
public sealed class AdapterPoolQueueCommandTests
{
    private const string PoolRtId = "670000000000000000000001";

    // ── GetAdapterPoolQueue ─────────────────────────────────────────────────

    [Fact]
    public async Task Queue_PrintsPositionInTenantAndTenantsAhead_NotAGlobalRank()
    {
        var console = new RecordingConsoleService();
        var command = NewQueueCommand(new FakeQueueClient(TwoTenantQueue()), console);
        command.CommandArgumentValue.ParseLayer(["-id", PoolRtId]);

        await command.Execute();

        var waiting = console.Lines.Where(l => l.Contains("WAITING")).ToList();
        Assert.Equal(3, waiting.Count);

        var second = Assert.Single(waiting, l => l.Contains("e-a2"));
        Assert.Contains("position=2 in its tenant", second);
        Assert.Contains("0 tenant(s) ahead in the rotation", second);

        var otherTenant = Assert.Single(waiting, l => l.Contains("e-b1"));
        Assert.Contains("position=1 in its tenant", otherTenant);
        Assert.Contains("1 tenant(s) ahead in the rotation", otherTenant);
        Assert.Contains("class=Interactive", otherTenant);

        // Nothing anywhere in the output claims a single queue-wide position.
        Assert.DoesNotContain(console.Lines, l => l.Contains("rank", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Queue_LeasedEntry_ShowsItsMemberAndSaysItCannotBeCancelledHere()
    {
        var console = new RecordingConsoleService();
        var command = NewQueueCommand(new FakeQueueClient(TwoTenantQueue()), console);
        command.CommandArgumentValue.ParseLayer(["-id", PoolRtId]);

        await command.Execute();

        var leased = Assert.Single(console.Lines, l => l.Contains("LEASED"));
        Assert.Contains("member=member-3", leased);
        Assert.Contains("e-run", leased);
        Assert.Contains(console.Lines,
            l => l.Contains("interrupting a running pipeline", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Queue_Empty_ReadsAsAnIdlePoolRatherThanAnError()
    {
        var console = new RecordingConsoleService();
        var logger = new RecordingLogger<GetAdapterPoolQueueCommand>();
        var command = NewQueueCommand(new FakeQueueClient([]), console, logger);
        command.CommandArgumentValue.ParseLayer(["-id", PoolRtId]);

        await command.Execute();

        Assert.Contains(console.Lines, l => l.Contains("empty queue"));
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Queue_WithJsonFlag_PrintsTheRawEntries()
    {
        var console = new RecordingConsoleService();
        var command = NewQueueCommand(new FakeQueueClient(TwoTenantQueue()), console);
        command.CommandArgumentValue.ParseLayer(["-id", PoolRtId, "-j"]);

        await command.Execute();

        var json = Assert.Single(console.Lines);
        Assert.Contains("\"PositionInTenant\":2", json);
        Assert.Contains("\"TenantsAheadInRotation\":1", json);
    }

    [Fact]
    public async Task Queue_WithoutTenantInContext_DoesNotCallTheService()
    {
        var client = new FakeQueueClient(TwoTenantQueue());
        var logger = new RecordingLogger<GetAdapterPoolQueueCommand>();
        var command = NewQueueCommand(client, new RecordingConsoleService(), logger, tenantId: null);
        command.CommandArgumentValue.ParseLayer(["-id", PoolRtId]);

        await command.Execute();

        Assert.Null(client.QueriedPoolRtId);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("TenantId"));
    }

    // ── CancelQueuedExecution ───────────────────────────────────────────────

    [Fact]
    public async Task Cancel_WaitingEntry_IsCancelled()
    {
        var client = new FakeQueueClient([], new AdapterPoolQueueCancellationResultDto
        {
            Outcome = AdapterPoolQueueCancellationOutcome.Cancelled
        });
        var logger = new RecordingLogger<CancelQueuedExecutionCommand>();
        var command = NewCancelCommand(client, logger, new FakeConfirmationService(true));
        command.CommandArgumentValue.ParseLayer(["-id", PoolRtId, "-eid", "e-a2"]);

        await command.Execute();

        Assert.Equal((PoolRtId, "e-a2"), client.CancelledEntry);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("was cancelled"));
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Cancel_AlreadyLeased_SaysInterruptingIsADifferentOperation()
    {
        var client = new FakeQueueClient([], new AdapterPoolQueueCancellationResultDto
        {
            Outcome = AdapterPoolQueueCancellationOutcome.AlreadyLeased,
            ServerMessage = "Execution 'e-run' already holds a lease and is no longer queued."
        });
        var logger = new RecordingLogger<CancelQueuedExecutionCommand>();
        var command = NewCancelCommand(client, logger, new FakeConfirmationService(true));
        command.CommandArgumentValue.ParseLayer(["-id", PoolRtId, "-eid", "e-run"]);

        await command.Execute();

        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("already holds a lease", warning.Message);
        Assert.Contains("different operation", warning.Message);
        Assert.Contains("nothing was", warning.Message, StringComparison.OrdinalIgnoreCase);
        // The 409 is its own case, so it must not be reported as a success.
        Assert.DoesNotContain(logger.Entries,
            e => e.Level == LogLevel.Information && e.Message.Contains("was cancelled"));
    }

    [Fact]
    public async Task Cancel_UnknownEntry_IsReportedAsNotFoundAndNotAsCancelled()
    {
        var client = new FakeQueueClient([], new AdapterPoolQueueCancellationResultDto
        {
            Outcome = AdapterPoolQueueCancellationOutcome.NotFound,
            ServerMessage = "No queued execution 'e-gone' belongs to adapter pool."
        });
        var logger = new RecordingLogger<CancelQueuedExecutionCommand>();
        var command = NewCancelCommand(client, logger, new FakeConfirmationService(true));
        command.CommandArgumentValue.ParseLayer(["-id", PoolRtId, "-eid", "e-gone"]);

        await command.Execute();

        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("No queued execution", warning.Message);
    }

    [Fact]
    public async Task Cancel_DeclinedConfirmation_NeverCallsTheService()
    {
        var client = new FakeQueueClient([]);
        var confirmation = new FakeConfirmationService(false);
        var command = NewCancelCommand(client, new RecordingLogger<CancelQueuedExecutionCommand>(), confirmation);
        command.CommandArgumentValue.ParseLayer(["-id", PoolRtId, "-eid", "e-a2"]);

        await Assert.ThrowsAsync<ToolException>(() => command.Execute());

        Assert.Null(client.CancelledEntry);
        // The prompt has to say what the verb does NOT do, or "yes" is not an informed answer.
        Assert.Contains("never run", confirmation.LastMessage);
        Assert.Contains("different operation", confirmation.LastMessage);
    }

    [Fact]
    public async Task Cancel_WithYesFlag_SkipsTheConfirmationPrompt()
    {
        var client = new FakeQueueClient([]);
        var confirmation = new FakeConfirmationService(false);
        var command = NewCancelCommand(client, new RecordingLogger<CancelQueuedExecutionCommand>(), confirmation);
        command.CommandArgumentValue.ParseLayer(["-id", PoolRtId, "-eid", "e-a2", "-y"]);

        await command.Execute();

        Assert.Equal((PoolRtId, "e-a2"), client.CancelledEntry);
        Assert.Null(confirmation.LastMessage);
    }

    // ── Fixtures ────────────────────────────────────────────────────────────

    /// <summary>
    ///     One leased entry plus two borrowing tenants waiting — the shape that makes a single global
    ///     rank impossible: borrower-b's first item runs before borrower-a's second one.
    /// </summary>
    private static List<AdapterPoolQueueEntryDto> TwoTenantQueue() =>
    [
        new()
        {
            ExecutionId = "e-run", BorrowerTenantId = "borrower-a", PipelineName = "Nightly",
            ExecutionClass = 1, QueuedAtUtc = new DateTime(2026, 9, 14, 7, 59, 0, DateTimeKind.Utc),
            PositionInTenant = 0, TenantsAheadInRotation = 0, LeasedOnMemberId = "member-3",
            LeaseExpiresAtUtc = new DateTime(2026, 9, 14, 8, 10, 0, DateTimeKind.Utc)
        },
        new()
        {
            ExecutionId = "e-a1", BorrowerTenantId = "borrower-a", PipelineName = "Nightly",
            ExecutionClass = 1, QueuedAtUtc = new DateTime(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc),
            PositionInTenant = 1, TenantsAheadInRotation = 0
        },
        new()
        {
            ExecutionId = "e-a2", BorrowerTenantId = "borrower-a", PipelineName = "Nightly",
            ExecutionClass = 1, QueuedAtUtc = new DateTime(2026, 9, 14, 8, 0, 5, DateTimeKind.Utc),
            PositionInTenant = 2, TenantsAheadInRotation = 0
        },
        new()
        {
            ExecutionId = "e-b1", BorrowerTenantId = "borrower-b", PipelineName = "Billing",
            ExecutionClass = 0, QueuedAtUtc = new DateTime(2026, 9, 14, 8, 0, 7, DateTimeKind.Utc),
            PositionInTenant = 1, TenantsAheadInRotation = 1
        }
    ];

    private static GetAdapterPoolQueueCommand NewQueueCommand(ICommunicationServicesClient client,
        IConsoleService consoleService,
        ILogger<GetAdapterPoolQueueCommand>? logger = null,
        string? tenantId = "lender") =>
        new(logger ?? new RecordingLogger<GetAdapterPoolQueueCommand>(),
            Options.Create(new OctoToolOptions { TenantId = tenantId }), consoleService, client,
            new FakeAuthenticationService());

    private static CancelQueuedExecutionCommand NewCancelCommand(ICommunicationServicesClient client,
        ILogger<CancelQueuedExecutionCommand> logger,
        IConfirmationService confirmationService,
        string? tenantId = "lender") =>
        new(logger, Options.Create(new OctoToolOptions { TenantId = tenantId }), client,
            new FakeAuthenticationService(), confirmationService);

    private sealed class RecordingConsoleService : IConsoleService
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

    private sealed class FakeConfirmationService(bool answer) : IConfirmationService
    {
        public string? LastMessage { get; private set; }

        public bool Confirm(string message)
        {
            LastMessage = message;
            return answer;
        }
    }

    private sealed class FakeAuthenticationService : IAuthenticationService
    {
        public Task EnsureAuthenticated(IServiceClientAccessToken serviceClientAccessToken) => Task.CompletedTask;

        public void SaveAuthenticationData(AuthenticationData authenticationData)
        {
        }
    }

    private sealed class FakeAccessToken : IServiceClientAccessToken
    {
        public string? AccessToken { get; set; }

        public event EventHandler? AccessTokenUpdated
        {
            add { }
            remove { }
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }

    /// <summary>
    ///     Only the two queue calls are implemented — everything else throws, so a command reaching
    ///     for anything but the queue endpoint fails the test instead of passing quietly.
    /// </summary>
    private sealed class FakeQueueClient(
        List<AdapterPoolQueueEntryDto> queue,
        AdapterPoolQueueCancellationResultDto? cancellation = null)
        : ICommunicationServicesClient
    {
        public string? QueriedPoolRtId { get; private set; }
        public (string PoolRtId, string ExecutionId)? CancelledEntry { get; private set; }

        public Task<IReadOnlyList<AdapterPoolQueueEntryDto>> GetAdapterPoolQueueAsync(string adapterPoolRtId)
        {
            QueriedPoolRtId = adapterPoolRtId;
            return Task.FromResult<IReadOnlyList<AdapterPoolQueueEntryDto>>(queue);
        }

        public Task<AdapterPoolQueueCancellationResultDto> CancelQueuedExecutionAsync(string adapterPoolRtId,
            string executionId)
        {
            CancelledEntry = (adapterPoolRtId, executionId);
            return Task.FromResult(cancellation ?? new AdapterPoolQueueCancellationResultDto
            {
                Outcome = AdapterPoolQueueCancellationOutcome.Cancelled
            });
        }

        public IServiceClientAccessToken AccessToken { get; } = new FakeAccessToken();
        public Uri ServiceUri { get; } = new("https://comm.example.com/lender/v1");

        public Task EnableAsync(string tenantId) => throw new NotSupportedException();
        public Task DisableAsync(string tenantId) => throw new NotSupportedException();

        public Task ReconfigureLogLevelAsync(string loggerName, LogLevelDto minLogLevel, LogLevelDto maxLogLevel) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AdapterSummaryDto>> GetAdaptersAsync() => throw new NotSupportedException();

        public Task<AdapterConfigurationDto> GetAdapterConfigurationAsync(string adapterRtId) =>
            throw new NotSupportedException();

        public Task<string> GetAdapterNodesAsync() => throw new NotSupportedException();
        public Task<string> GetPipelineSchemaAsync(string adapterRtId) => throw new NotSupportedException();

        public Task<RotateServiceAccountSecretResultDto> RotateServiceAccountSecretAsync(string adapterRtId) =>
            throw new NotSupportedException();

        public Task<DeploymentResultDto> GetPipelineDeploymentStateAsync(string pipelineRtId) =>
            throw new NotSupportedException();

        public Task DeployPipelineAsync(string adapterRtId, string pipelineRtId, string pipelineDefinition) =>
            throw new NotSupportedException();

        public Task<string> ExecutePipelineAsync(string pipelineRtId, string? pipelineInput, bool isDryRun = false) =>
            throw new NotSupportedException();

        public Task<SetPipelineDebugResultDto> SetPipelineDebuggingAsync(string pipelineRtId, bool enabled) =>
            throw new NotSupportedException();

        public Task<PipelineDebugStateDto> GetPipelineDebuggingAsync(string pipelineRtId) =>
            throw new NotSupportedException();

        public Task<IEnumerable<PipelineExecutionDataDto>> GetPipelineExecutionsAsync(string pipelineRtId) =>
            throw new NotSupportedException();

        public Task<PipelineExecutionDataDto> GetLatestPipelineExecutionAsync(string pipelineRtId) =>
            throw new NotSupportedException();

        public Task<string> GetPipelineExecutionDebugPointsAsync(string pipelineRtId, Guid executionId) =>
            throw new NotSupportedException();

        public Task<DebugPointDataDto> GetDebugPointAsync(string pipelineRtId, Guid executionId, string nodeId) =>
            throw new NotSupportedException();

        public Task DeployTriggersAsync() => throw new NotSupportedException();
        public Task UndeployTriggersAsync() => throw new NotSupportedException();
        public Task<IReadOnlyList<DeploymentSiteSummaryDto>> GetDeploymentSitesAsync() => throw new NotSupportedException();
        public Task DeployDeploymentSiteAsync(string poolRtId) => throw new NotSupportedException();
        public Task UndeployDeploymentSiteAsync(string poolRtId) => throw new NotSupportedException();
        public Task DeployDataFlowAsync(string dataFlowRtId) => throw new NotSupportedException();
        public Task UndeployDataFlowAsync(string dataFlowRtId) => throw new NotSupportedException();

        public Task<DataFlowStatusDto> GetDataFlowStatusAsync(string dataFlowRtId) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<WorkloadSummaryDto>> GetWorkloadsByChartAsync(string chartName) =>
            throw new NotSupportedException();

        public Task UpdateWorkloadChartVersionAsync(string workloadRtId, string chartVersion) =>
            throw new NotSupportedException();

        public Task DeployWorkloadAsync(string workloadRtId) => throw new NotSupportedException();
        public Task UndeployWorkloadAsync(string workloadRtId) => throw new NotSupportedException();

        public Task<MovePipelinesToAdapterResponseDto> MovePipelinesToAdapterAsync(
            MovePipelinesToAdapterRequestDto request) => throw new NotSupportedException();

        public Task<CommunicationLifecycleDto> GetLifecycleAsync() => throw new NotSupportedException();

        public Task<CommunicationLifecycleDto> SetLifecycleAsync(CommunicationLifecycleDto lifecycle) =>
            throw new NotSupportedException();
    }
}
