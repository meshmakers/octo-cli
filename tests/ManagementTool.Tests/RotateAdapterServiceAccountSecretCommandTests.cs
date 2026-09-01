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
/// AB#5048 — the CLI surface of the pipeline service-account secret rotation (AB#5032).
/// Two things are pinned here, because both are the kind that quietly stop working:
/// the destructive-verb confirmation gate, and the redeploy hint. Without the hint the operator
/// walks away from a rotation that has invalidated the credential every running pipeline still
/// presents, and sees "rotation done, still broken".
/// </summary>
public sealed class RotateAdapterServiceAccountSecretCommandTests
{
    private const string AdapterRtId = "69cfa838092b710403248acd";

    [Fact]
    public async Task Rotate_SurfacesTheRedeployRequirementAsAWarning()
    {
        var client = new FakeCommunicationServicesClient(Rotated());
        var logger = new RecordingLogger<RotateAdapterServiceAccountSecretCommand>();
        var command = NewCommand(client, logger, new FakeConfirmationService(true));
        command.CommandArgumentValue.ParseLayer(["-id", AdapterRtId]);

        await command.Execute();

        Assert.Equal(AdapterRtId, client.RotatedAdapterRtId);
        var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        Assert.Single(warnings);
        Assert.Contains("Redeploy required", warnings[0].Message);
        // The controller's own message is relayed verbatim rather than reworded, so CLI and audit
        // event cannot drift apart.
        Assert.Contains(logger.Entries, e => e.Message.Contains("Redeploy the pipelines"));
    }

    [Fact]
    public async Task Rotate_FirstProvisioning_DoesNotClaimARedeployIsNeeded()
    {
        // Nothing was running under the old credential, so demanding a redeploy here would be noise
        // the operator learns to ignore — exactly how the real warning gets missed later.
        var client = new FakeCommunicationServicesClient(FirstProvisioning());
        var logger = new RecordingLogger<RotateAdapterServiceAccountSecretCommand>();
        var command = NewCommand(client, logger, new FakeConfirmationService(true));
        command.CommandArgumentValue.ParseLayer(["-id", AdapterRtId]);

        await command.Execute();

        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains(logger.Entries, e => e.Message.Contains("Nothing was invalidated"));
    }

    [Fact]
    public async Task Rotate_DeclinedConfirmation_NeverCallsTheService()
    {
        var client = new FakeCommunicationServicesClient(Rotated());
        var confirmation = new FakeConfirmationService(false);
        var command = NewCommand(client, new RecordingLogger<RotateAdapterServiceAccountSecretCommand>(),
            confirmation);
        command.CommandArgumentValue.ParseLayer(["-id", AdapterRtId]);

        await Assert.ThrowsAsync<ToolException>(() => command.Execute());

        Assert.Null(client.RotatedAdapterRtId);
        Assert.Contains("rotate the pipeline service account secret", confirmation.LastMessage);
        // "Yes" is only an informed answer if it says what is lost and what is still owed.
        Assert.Contains("stops working immediately", confirmation.LastMessage);
        Assert.Contains("redeployed", confirmation.LastMessage);
    }

    [Fact]
    public async Task Rotate_WithYesFlag_SkipsTheConfirmationPrompt()
    {
        var client = new FakeCommunicationServicesClient(Rotated());
        // Would decline if asked — so a call reaching the service proves the prompt was skipped.
        var confirmation = new FakeConfirmationService(false);
        var command = NewCommand(client, new RecordingLogger<RotateAdapterServiceAccountSecretCommand>(),
            confirmation);
        command.CommandArgumentValue.ParseLayer(["-id", AdapterRtId, "-y"]);

        await command.Execute();

        Assert.Equal(AdapterRtId, client.RotatedAdapterRtId);
        Assert.Null(confirmation.LastMessage);
    }

    [Fact]
    public async Task Rotate_WithoutTenantInContext_DoesNotCallTheService()
    {
        var client = new FakeCommunicationServicesClient(Rotated());
        var logger = new RecordingLogger<RotateAdapterServiceAccountSecretCommand>();
        var command = NewCommand(client, logger, new FakeConfirmationService(true), tenantId: null);
        command.CommandArgumentValue.ParseLayer(["-id", AdapterRtId]);

        await command.Execute();

        Assert.Null(client.RotatedAdapterRtId);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("TenantId"));
    }

    private static RotateServiceAccountSecretResultDto Rotated() =>
        new("octo-pipeline-sa-1", "pipeline-service-account-1", WasCreated: false,
            RequiresPipelineRedeploy: true,
            "The client secret of pipeline service account 'octo-pipeline-sa-1' was rotated. " +
            "Redeploy the pipelines / data flows of this adapter.");

    private static RotateServiceAccountSecretResultDto FirstProvisioning() =>
        new("octo-pipeline-sa-1", "pipeline-service-account-1", WasCreated: true,
            RequiresPipelineRedeploy: false,
            "Adapter 'mesh-adapter' had no pipeline service account; 'octo-pipeline-sa-1' was " +
            "provisioned instead. Nothing was invalidated.");

    private static RotateAdapterServiceAccountSecretCommand NewCommand(
        ICommunicationServicesClient client,
        ILogger<RotateAdapterServiceAccountSecretCommand> logger,
        IConfirmationService confirmationService,
        string? tenantId = "acme") =>
        new(logger, Options.Create(new OctoToolOptions { TenantId = tenantId }), client,
            new FakeAuthenticationService(), confirmationService);

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

        // Never raised — nothing under test reads the token, the interface just requires it.
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
    /// Only the rotation call is implemented — the rest of the interface exists so the command can be
    /// constructed. A member that throws is the point: the command must not reach for anything else.
    /// </summary>
    private sealed class FakeCommunicationServicesClient(RotateServiceAccountSecretResultDto result)
        : ICommunicationServicesClient
    {
        public string? RotatedAdapterRtId { get; private set; }

        public Task<RotateServiceAccountSecretResultDto> RotateServiceAccountSecretAsync(string adapterRtId)
        {
            RotatedAdapterRtId = adapterRtId;
            return Task.FromResult(result);
        }

        public IServiceClientAccessToken AccessToken { get; } = new FakeAccessToken();
        public Uri ServiceUri { get; } = new("https://comm.example.com/acme/v1");

        public Task EnableAsync(string tenantId) => throw new NotSupportedException();
        public Task DisableAsync(string tenantId) => throw new NotSupportedException();

        public Task ReconfigureLogLevelAsync(string loggerName, LogLevelDto minLogLevel, LogLevelDto maxLogLevel) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AdapterSummaryDto>> GetAdaptersAsync() => throw new NotSupportedException();

        public Task<AdapterConfigurationDto> GetAdapterConfigurationAsync(string adapterRtId) =>
            throw new NotSupportedException();

        public Task<string> GetAdapterNodesAsync() => throw new NotSupportedException();
        public Task<string> GetPipelineSchemaAsync(string adapterRtId) => throw new NotSupportedException();

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
        public Task<IReadOnlyList<PoolSummaryDto>> GetPoolsAsync() => throw new NotSupportedException();
        public Task DeployPoolAsync(string poolRtId) => throw new NotSupportedException();
        public Task UndeployPoolAsync(string poolRtId) => throw new NotSupportedException();
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
