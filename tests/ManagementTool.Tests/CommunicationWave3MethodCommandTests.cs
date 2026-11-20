using FakeItEasy;
using GraphQL;
using GraphQlDtos.DataTransferObjects.System.Communication.v4;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication.CommunicationMethodCall;

namespace ManagementTool.Tests;

/// <summary>
///     The communication write commands (pipeline deploy, execute, debug and move, data flow, workload, deployment site and
///     trigger deploy/undeploy, workload chart version, queued execution cancel, service account secret rotation) run the
///     CK methods of <c>System.Communication</c> through the method clients generated in GraphQlDtos
///     (<c>System.Communication-[4.19,5.0)</c>): every command sends its method mutation addressed by runtime id and never
///     calls a REST write of <see cref="ICommunicationServicesClient" />. Names, arguments and output stay as they were; a
///     refused call ends with the code (exit -3), a service without the methods with a message naming the version.
/// </summary>
public class CommunicationWave3MethodCommandTests
{
    private const string AdapterRtId = "65f0c0ffee00000000000101";
    private const string PipelineRtId = "65f0c0ffee00000000000102";
    private const string SecondPipelineRtId = "65f0c0ffee00000000000103";
    private const string DataFlowRtId = "65f0c0ffee00000000000104";
    private const string SiteRtId = "65f0c0ffee00000000000105";
    private const string WorkloadRtId = "65f0c0ffee00000000000106";
    private const string PoolRtId = "65f0c0ffee00000000000107";

    private readonly ICommunicationServicesClient _communication = A.Fake<ICommunicationServicesClient>();
    private readonly ITenantClient _tenant = A.Fake<ITenantClient>();
    private readonly IAuthenticationService _auth = A.Fake<IAuthenticationService>();
    private readonly IConfirmationService _confirmation = A.Fake<IConfirmationService>();
    private readonly List<string> _consoleLines = [];
    private readonly List<GraphQLRequest> _requests = [];
    private readonly List<GraphQLRequest> _queries = [];
    private string? _workloadCkTypeId = "System.Communication/Application-1";
    private readonly string _definitionFile;

    /// <summary>Command, arguments, the GraphQL path of its method and the runtime id the call must carry.</summary>
    public static readonly Dictionary<string, (Type Command, string[] Args, string Field, string RtId)> Cases = new()
    {
        ["DeployPipeline"] = (typeof(DeployPipelineCommand), ["-aid", AdapterRtId, "-pid", PipelineRtId, "-f", "<file>"],
            "systemCommunicationPipelines { deploy(", PipelineRtId),
        ["ExecutePipeline"] = (typeof(ExecutePipelineCommand), ["-id", PipelineRtId],
            "systemCommunicationPipelines { execute(", PipelineRtId),
        ["SetPipelineDebug"] = (typeof(SetPipelineDebugCommand), ["-id", PipelineRtId, "-e", "true"],
            "systemCommunicationPipelines { setDebugging(", PipelineRtId),
        ["MovePipelinesToAdapter"] = (typeof(MovePipelinesToAdapterCommand), ["-ids", PipelineRtId, "-aid", AdapterRtId, "-y"],
            "systemCommunicationPipelines { moveToAdapter(", PipelineRtId),
        ["DeployDataFlow"] = (typeof(DeployDataFlowCommand), ["-id", DataFlowRtId],
            "systemCommunicationDataFlows { deploy(", DataFlowRtId),
        ["UndeployDataFlow"] = (typeof(UndeployDataFlowCommand), ["-id", DataFlowRtId],
            "systemCommunicationDataFlows { undeploy(", DataFlowRtId),
        ["DeployDeploymentSite"] = (typeof(DeployDeploymentSiteCommand), ["-id", SiteRtId],
            "systemCommunicationDeploymentSites { deploy(", SiteRtId),
        ["UndeployDeploymentSite"] = (typeof(UndeployDeploymentSiteCommand), ["-id", SiteRtId, "-y"],
            "systemCommunicationDeploymentSites { undeploy(", SiteRtId),
        ["DeployTriggers"] = (typeof(DeployTriggersCommand), [],
            "systemCommunicationPipelineTriggers { deployAll(", ""),
        ["UndeployTriggers"] = (typeof(UndeployTriggersCommand), [],
            "systemCommunicationPipelineTriggers { undeployAll(", ""),
        ["DeployWorkload"] = (typeof(DeployWorkloadCommand), ["-id", WorkloadRtId],
            "systemCommunicationApplications { deploy(", WorkloadRtId),
        ["UndeployWorkload"] = (typeof(UndeployWorkloadCommand), ["-id", WorkloadRtId, "-y"],
            "systemCommunicationApplications { undeploy(", WorkloadRtId),
        ["UpdateWorkloadChartVersion"] = (typeof(UpdateWorkloadChartVersionCommand), ["-id", WorkloadRtId, "-cv", "1.2.3"],
            "systemCommunicationApplications { setChartVersion(", WorkloadRtId),
        ["CancelQueuedExecution"] = (typeof(CancelQueuedExecutionCommand), ["-id", PoolRtId, "-eid", "e-1", "-y"],
            "systemCommunicationAdapterPools { cancelQueuedExecution(", PoolRtId),
        ["RotateAdapterServiceAccountSecret"] = (typeof(RotateAdapterServiceAccountSecretCommand), ["-id", AdapterRtId, "-y"],
            "systemCommunicationAdapters { rotateServiceAccountSecret(", AdapterRtId),
    };

    public static TheoryData<string> CaseNames => new(Cases.Keys);

    public CommunicationWave3MethodCommandTests()
    {
        _definitionFile = Path.Combine(Path.GetTempPath(), $"octo-cli-pipeline-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(_definitionFile, "triggers: []\ntransformations: []\n");
        A.CallTo(() => _confirmation.Confirm(A<string>._)).Returns(true);
        A.CallTo(() => _communication.ServiceUri).Returns(new Uri("https://comm.example.com/acme/v1"));
        A.CallTo(() => _tenant.SendQueryAsync<WorkloadTypeDto>(A<GraphQLRequest>._)).ReturnsLazily(
            (GraphQLRequest request) =>
            {
                _queries.Add(request);
                return Task.FromResult<QlItemsContainer<WorkloadTypeDto>?>(new QlItemsContainer<WorkloadTypeDto>
                {
                    Items = _workloadCkTypeId == null
                        ? []
                        : [new WorkloadTypeDto { RtId = WorkloadRtId, CkTypeId = _workloadCkTypeId }]
                });
            });
        MethodFakes.AnswerMethodCalls(_tenant, _requests, null);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task Command_RunsItsMethod_ByRuntimeId_AndCallsNoRestWrite(string name)
    {
        var (type, args, field, rtId) = Cases[name];
        var command = Create(type, args);

        await command.Execute();

        var request = Assert.Single(_requests);
        Assert.Contains(field, request.Query, StringComparison.Ordinal);
        Assert.Contains(rtId, Variables(request), StringComparison.Ordinal);
        A.CallTo(() => _auth.EnsureAuthenticated(_tenant.AccessToken)).MustHaveHappened();
        AssertNoRestWrite();
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task Command_RefusedByTheMethodGateway_FailsWithTheCode_AndCallsNoRestWrite(string name)
    {
        var (type, args, _, _) = Cases[name];
        MethodFakes.AnswerMethodCalls(_tenant, _requests, "METHOD_FORBIDDEN",
            parameters: new Dictionary<string, object?> { ["reason"] = "role-missing" });
        var command = Create(type, args);

        var ex = await Assert.ThrowsAsync<CkMethodCallException>(() => command.Execute());

        Assert.Equal("METHOD_FORBIDDEN", ex.Code);
        Assert.Contains("reason=role-missing", CkMethodErrorText.Format(ex));
        AssertNoRestWrite();
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task ServerWithoutTheMethods_NamesTheNeededVersion_AndCallsNoRestWrite(string name)
    {
        var (type, args, _, _) = Cases[name];
        MethodFakes.AnswerMethodCallsWithValidationErrors(_tenant, _requests, "SystemCommunicationMethodInput", "method");
        var command = Create(type, args);

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() => command.Execute());

        Assert.Contains("System.Communication 4.19.0 or newer", ex.Message);
        Assert.Contains("does not fall back to the REST endpoint", ex.Message);
        AssertNoRestWrite();
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task NoHandlerOfTheMethod_NamesTheNeededVersion_AndCallsNoRestWrite(string name)
    {
        var (type, args, _, _) = Cases[name];
        MethodFakes.AnswerMethodCalls(_tenant, _requests, "METHOD_HANDLER_NOT_AVAILABLE");
        var command = Create(type, args);

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() => command.Execute());

        Assert.Contains("System.Communication 4.19.0 or newer", ex.Message);
        AssertNoRestWrite();
    }

    [Theory]
    [InlineData("DeployWorkload", "WORKLOAD_DEPLOY_FAILED")]
    [InlineData("UpdateWorkloadChartVersion", "WORKLOAD_CHART_VERSION_UPDATE_FAILED")]
    [InlineData("DeployDataFlow", "DATA_FLOW_DEPLOY_FAILED")]
    [InlineData("DeployPipeline", "PIPELINE_DEPLOY_REJECTED")]
    [InlineData("DeployDeploymentSite", "COMMUNICATION_DISABLED")]
    public async Task DeclaredError_FailsWithCodeMessageAndParameters(string name, string code)
    {
        var (type, args, _, _) = Cases[name];
        MethodFakes.AnswerMethodCalls(_tenant, _requests, code, "The operation failed.",
            new Dictionary<string, object?> { ["reason"] = "adapter-rejected" });

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() => Create(type, args).Execute());

        Assert.Equal($"{code}: The operation failed. (reason=adapter-rejected)", ex.Message);
        AssertNoRestWrite();
    }

    [Theory]
    [InlineData("DeployWorkload")]
    [InlineData("UpdateWorkloadChartVersion")]
    public async Task ARefusedCdCall_ThroughTheRunner_PrintsCodeAndParameters_AndExitsWithMinusThree(string name)
    {
        // The deployment pipelines (deploy-workload.yml) judge these two commands by their exit code only.
        var (type, args, _, _) = Cases[name];
        MethodFakes.AnswerMethodCalls(_tenant, _requests, "METHOD_FORBIDDEN",
            parameters: new Dictionary<string, object?> { ["reason"] = "role-missing" });
        var command = Create(type, args);
        var parser = A.Fake<ICommandParser>();
        A.CallTo(() => parser.ParseAndValidateAsync(A<string?>._))
            .Returns(Task.Run(command.Execute, TestContext.Current.CancellationToken));
        var contexts = A.Fake<IContextManager>();
        A.CallTo(() => contexts.GetEffectiveContextName()).Returns("test");
        var logger = new RecordingLogger<Runner>();

        var exitCode = await new Runner(logger, parser, contexts, ContextSelection.None).DoActionAsync();

        Assert.Equal(-3, exitCode);
        var error = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains("METHOD_FORBIDDEN", error.Message);
        Assert.Contains("reason=role-missing", error.Message);
        AssertNoRestWrite();
    }

    [Theory]
    [InlineData("System.Communication/Adapter", "systemCommunicationAdapters { deploy(")]
    [InlineData("System.Communication/Adapter-1", "systemCommunicationAdapters { deploy(")]
    [InlineData("System.Communication/AdapterPool-1", "systemCommunicationAdapterPools { deploy(")]
    [InlineData("System.Communication/AdapterPool-2", "systemCommunicationAdapterPools { deploy(")]
    [InlineData("System.Communication/Application-2", "systemCommunicationApplications { deploy(")]
    [InlineData("System.Communication/Application-17", "systemCommunicationApplications { deploy(")]
    public async Task DeployWorkload_UsesTheClientOfTheConcreteType_WhateverTheElementVersion(string ckTypeId,
        string field)
    {
        _workloadCkTypeId = ckTypeId;

        await Create(typeof(DeployWorkloadCommand), ["-id", WorkloadRtId]).Execute();

        var query = Assert.Single(_queries);
        Assert.Contains(WorkloadRtId, Variables(query), StringComparison.Ordinal);
        Assert.Contains(field, Assert.Single(_requests).Query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("System.Communication/Adapter", "Adapter")]
    [InlineData("System.Communication/Adapter-1", "Adapter")]
    [InlineData("System.Communication/Adapter-2", "Adapter")]
    [InlineData("System.Communication/AdapterPool-2", "AdapterPool")]
    [InlineData("System.Communication/Application-3", "Application")]
    [InlineData("System.Communication/Pipeline-1", null)]
    [InlineData("System.Communication/Adapter-", null)]
    [InlineData("Other.Model/Adapter-1", null)]
    [InlineData("System.Communication/MyAdapter-1", null)]
    public void ParseWorkloadKind_ComparesTheVersionLessType(string ckTypeId, string? expected)
    {
        Assert.Equal(expected, ParseWorkloadKind(ckTypeId)?.ToString());
    }

    [Fact]
    public async Task UnknownWorkload_FailsBeforeAnyWrite()
    {
        _workloadCkTypeId = null;

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() =>
            Create(typeof(DeployWorkloadCommand), ["-id", WorkloadRtId]).Execute());

        Assert.Equal($"Workload '{WorkloadRtId}' not found.", ex.Message);
        Assert.Empty(_requests);
        AssertNoRestWrite();
    }

    [Fact]
    public async Task WorkloadOfAnotherType_FailsBeforeAnyWrite()
    {
        _workloadCkTypeId = "System.Communication/Pipeline-1";

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() =>
            Create(typeof(UpdateWorkloadChartVersionCommand), ["-id", WorkloadRtId, "-cv", "1.2.3"]).Execute());

        Assert.Contains("no Adapter, AdapterPool or Application", ex.Message);
        Assert.Empty(_requests);
    }

    [Theory]
    [InlineData("DeployWorkload", "not-an-id")]
    [InlineData("DeployDataFlow", "not-an-id")]
    [InlineData("ExecutePipeline", "123")]
    [InlineData("RotateAdapterServiceAccountSecret", "abc")]
    public async Task AnInvalidRuntimeId_FailsBeforeAnyCall_InsteadOfTheVersionMessage(string name, string id)
    {
        var (type, args, _, _) = Cases[name];
        var withId = args.Select(a => a is AdapterRtId or PipelineRtId or DataFlowRtId or WorkloadRtId ? id : a).ToArray();

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() => Create(type, withId).Execute());

        Assert.EndsWith($"'{id}' is not a valid id.", ex.Message);
        Assert.Empty(_requests);
        Assert.Empty(_queries);
    }

    [Fact]
    public async Task WorkloadTypeLookup_IsValidAgainstTheAssetRepositorySchema()
    {
        var errors = await AssetRepositoryGraphQlSchema.ValidateAgainstSystemBotAsync(
            GraphQlDtos.GraphQlConstants.GetDeployableWorkloadCkTypeById, $"{{\"rtId\":\"{WorkloadRtId}\"}}");

        Assert.Empty(errors);
    }

    [Fact]
    public async Task UpdateWorkloadChartVersion_And_DeployWorkload_LogTheLinesTheDeploymentPipelinesSaw()
    {
        // deploy-workload.yml logs these lines; they must stay as they were before the methods.
        var chartLogger = new RecordingLogger<UpdateWorkloadChartVersionCommand>();
        await Create(typeof(UpdateWorkloadChartVersionCommand), ["-id", WorkloadRtId, "-cv", "1.2.3"], chartLogger)
            .Execute();
        var deployLogger = new RecordingLogger<DeployWorkloadCommand>();
        MethodFakes.AnswerMethodCalls(_tenant, _requests, null,
            result: new RtWorkloadDeployResultRecordDto { Warning = "MemoryRequest 512Mi is overridden" });
        await Create(typeof(DeployWorkloadCommand), ["-id", WorkloadRtId], deployLogger).Execute();

        Assert.Equal(
        [
            $"Setting chart version of workload '{WorkloadRtId}' to '1.2.3' for tenant 'acme' at 'https://comm.example.com/acme/v1'",
            $"Chart version for workload '{WorkloadRtId}' updated to '1.2.3'. Deploy is NOT triggered — run DeployWorkload separately if needed."
        ], chartLogger.Entries.Where(e => e.Level >= LogLevel.Information).Select(e => e.Message));
        Assert.Equal(
        [
            $"Deploying workload '{WorkloadRtId}' for tenant 'acme' at 'https://comm.example.com/acme/v1'",
            $"Workload '{WorkloadRtId}' deploy triggered"
        ], deployLogger.Entries.Where(e => e.Level >= LogLevel.Information).Select(e => e.Message));
        Assert.Empty(_consoleLines);
    }

    [Fact]
    public async Task UpdateWorkloadChartVersion_SendsTheVersion()
    {
        await Create(typeof(UpdateWorkloadChartVersionCommand), ["-id", WorkloadRtId, "-cv", "1.2.3"]).Execute();

        Assert.Contains("\"chartVersion\":\"1.2.3\"", Variables(Assert.Single(_requests)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecutePipeline_PrintsOnlyTheExecutionId()
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, null,
            result: new RtPipelineExecutionStartResultRecordDto { ExecutionId = "4f6a6f0c-0000-4000-8000-000000000001" });

        await Create(typeof(ExecutePipelineCommand), ["-id", PipelineRtId]).Execute();

        Assert.Equal(["4f6a6f0c-0000-4000-8000-000000000001"], _consoleLines);
        Assert.Contains("\"isDryRun\":false", Variables(Assert.Single(_requests)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeployPipeline_SendsTheDefinitionAndTheAdapter()
    {
        await Create(typeof(DeployPipelineCommand), Cases["DeployPipeline"].Args).Execute();

        var variables = Variables(Assert.Single(_requests));
        Assert.Contains(AdapterRtId, variables, StringComparison.Ordinal);
        Assert.Contains("transformations: []", variables, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, LogLevel.Information, "applied to the running adapter")]
    [InlineData(false, LogLevel.Warning, "the adapter is offline")]
    public async Task SetPipelineDebug_ReportsWhetherTheRunningAdapterGotIt(bool applied, LogLevel level, string text)
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, null,
            result: new RtPipelineDebugResultRecordDto { Enabled = true, AppliedToRunningAdapter = applied });
        var logger = new RecordingLogger<SetPipelineDebugCommand>();

        await Create(typeof(SetPipelineDebugCommand), ["-id", PipelineRtId, "-e", "true"], logger).Execute();

        var last = logger.Entries.Last();
        Assert.Equal(level, last.Level);
        Assert.Contains(text, last.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MovePipelinesToAdapter_CallsTheMethodOncePerPipeline_AndLogsEachMove()
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, null,
            result: new RtPipelineMoveResultRecordDto { OldAdapterRtId = "old", NewAdapterRtId = AdapterRtId });
        var logger = new RecordingLogger<MovePipelinesToAdapterCommand>();

        await Create(typeof(MovePipelinesToAdapterCommand),
            ["-ids", $"{PipelineRtId},{SecondPipelineRtId}", "-aid", AdapterRtId, "-rd", "-y"], logger).Execute();

        Assert.Equal(2, _requests.Count);
        Assert.Contains(PipelineRtId, Variables(_requests[0]), StringComparison.Ordinal);
        Assert.Contains(SecondPipelineRtId, Variables(_requests[1]), StringComparison.Ordinal);
        Assert.All(_requests, r => Assert.Contains("\"redeploy\":true", Variables(r), StringComparison.Ordinal));
        Assert.Contains(logger.Entries, e => e.Message == $"Pipeline {PipelineRtId} moved old -> {AdapterRtId}");
        Assert.Equal("Move summary: 2 succeeded, 0 failed (of 2)", logger.Entries.Last().Message);
    }

    [Fact]
    public async Task MovePipelinesToAdapter_AFailedMove_IsReportedPerPipeline_AndFailsTheCommand()
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, "PIPELINE_MOVE_FAILED", "The adapter is unknown.");
        var logger = new RecordingLogger<MovePipelinesToAdapterCommand>();

        var ex = await Assert.ThrowsAsync<ToolException>(() => Create(typeof(MovePipelinesToAdapterCommand),
            ["-ids", $"{PipelineRtId},{SecondPipelineRtId}", "-aid", AdapterRtId, "-y"], logger).Execute());

        Assert.Equal("2 pipeline move(s) failed — see log above", ex.Message);
        Assert.Equal(2, _requests.Count);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error &&
                                             e.Message.Contains("PIPELINE_MOVE_FAILED: The adapter is unknown."));
    }

    [Fact]
    public async Task MovePipelinesToAdapter_AnUnknownPipeline_IsAFailedItem_NotTheEndOfTheList()
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, "METHOD_TARGET_NOT_FOUND");
        var logger = new RecordingLogger<MovePipelinesToAdapterCommand>();

        await Assert.ThrowsAsync<ToolException>(() => Create(typeof(MovePipelinesToAdapterCommand),
            ["-ids", $"{PipelineRtId},not-an-id", "-aid", AdapterRtId, "-y"], logger).Execute());

        Assert.Single(_requests);
        Assert.Contains(logger.Entries, e => e.Message.Contains($"Pipeline '{PipelineRtId}' was not found"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("PipelineRtId 'not-an-id' is not a valid id"));
    }

    [Fact]
    public async Task RotateAdapterServiceAccountSecret_PrintsNoSecret_AndRelaysTheMessage()
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, null, result: new RtServiceAccountSecretRotationResultRecordDto
        {
            ClientId = "octo-pipeline-sa-1", ConfigurationWellKnownName = "pipeline-service-account-1",
            WasCreated = false, RequiresPipelineRedeploy = true, Message = "Rotated. Redeploy the pipelines."
        });
        var logger = new RecordingLogger<RotateAdapterServiceAccountSecretCommand>();

        await Create(typeof(RotateAdapterServiceAccountSecretCommand), ["-id", AdapterRtId, "-y"], logger).Execute();

        Assert.Contains(logger.Entries, e => e.Message == "Rotated. Redeploy the pipelines.");
        Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Empty(_consoleLines);
    }

    private ServiceClientOctoCommand<ICommunicationServicesClient> Create(Type type, string[] args,
        ILogger? logger = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(Options.Create(new OctoToolOptions { TenantId = "acme" }));
        services.AddSingleton(_communication);
        services.AddSingleton(_tenant);
        services.AddSingleton(_auth);
        services.AddSingleton(_confirmation);
        var console = A.Fake<IConsoleService>();
        A.CallTo(() => console.WriteLine(A<string>._)).Invokes((string line) => _consoleLines.Add(line));
        services.AddSingleton(console);
        var provider = services.BuildServiceProvider();
        var command = (ServiceClientOctoCommand<ICommunicationServicesClient>)(logger == null
            ? ActivatorUtilities.CreateInstance(provider, type)
            : ActivatorUtilities.CreateInstance(provider, type, logger));
        command.CommandArgumentValue.ParseLayer(args.Select(a => a == "<file>" ? _definitionFile : a).ToArray());
        return command;
    }

    private void AssertNoRestWrite() =>
        A.CallTo(_communication).Where(c => IsRestWrite(c)).MustNotHaveHappened();

    private static bool IsRestWrite(FakeItEasy.Core.IFakeObjectCall call) =>
        !call.Method.IsSpecialName && !call.Method.Name.StartsWith("Get", StringComparison.Ordinal);

    private static string Variables(GraphQLRequest request) =>
        System.Text.Json.JsonSerializer.Serialize(request.Variables);
}
