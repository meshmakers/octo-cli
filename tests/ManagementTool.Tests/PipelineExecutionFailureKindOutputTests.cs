using FakeItEasy;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#8156 — GetLatestPipelineExecution and GetPipelineExecutions show the failure kind of AB#8037, as its name.
/// </summary>
public sealed class PipelineExecutionFailureKindOutputTests
{
    private const string PipelineId = "cc0000000000000000000003";

    private static PipelineExecutionDataDto Failed(PipelineExecutionFailureKind? kind) => new()
    {
        Id = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
        DateTime = new DateTime(2026, 11, 5, 8, 0, 0, DateTimeKind.Utc),
        Status = PipelineExecutionStatus.Failed,
        ErrorMessage = "write not acknowledged",
        FailureKind = kind
    };

    [Fact]
    public async Task GetLatest_OutcomeUnknown_PrintsTheKindName()
    {
        var (command, client, console) = NewLatest();
        A.CallTo(() => client.GetLatestPipelineExecutionAsync(PipelineId))
            .Returns(Failed(PipelineExecutionFailureKind.OutcomeUnknown));
        command.CommandArgumentValue.ParseLayer(["-id", PipelineId]);

        await command.Execute();

        var output = string.Join('\n', console.Lines);
        Assert.Contains("\"FailureKind\": \"OutcomeUnknown\"", output);
        Assert.DoesNotContain("\"FailureKind\": 1", output);
    }

    [Fact]
    public async Task GetLatest_Json_PrintsTheKindNameCompact()
    {
        var (command, client, console) = NewLatest();
        A.CallTo(() => client.GetLatestPipelineExecutionAsync(PipelineId))
            .Returns(Failed(PipelineExecutionFailureKind.Failed));
        command.CommandArgumentValue.ParseLayer(["-id", PipelineId, "-j"]);

        await command.Execute();

        var line = Assert.Single(console.Lines);
        Assert.Contains("\"FailureKind\":\"Failed\"", line);
        Assert.DoesNotContain('\n', line);
    }

    [Fact]
    public async Task GetLatest_NoKind_PrintsNull_AndStatusKeepsItsRendering()
    {
        var (command, client, console) = NewLatest();
        A.CallTo(() => client.GetLatestPipelineExecutionAsync(PipelineId)).Returns(Failed(null));
        command.CommandArgumentValue.ParseLayer(["-id", PipelineId]);

        await command.Execute();

        var output = string.Join('\n', console.Lines);
        Assert.Contains("\"FailureKind\": null", output);
        Assert.Contains("\"Status\": 2", output); // unchanged: the enum number as before AB#8156
    }

    [Fact]
    public async Task GetExecutions_PrintsTheKindOfEveryFailedExecution()
    {
        var console = new RecordingConsole();
        var client = A.Fake<ICommunicationServicesClient>();
        A.CallTo(() => client.GetPipelineExecutionsAsync(PipelineId)).Returns(new[]
        {
            Failed(PipelineExecutionFailureKind.OutcomeUnknown),
            Failed(PipelineExecutionFailureKind.Failed),
            Failed(null)
        });
        var command = new GetPipelineExecutionsCommand(NullLogger<GetPipelineExecutionsCommand>.Instance,
            Options.Create(new OctoToolOptions { TenantId = "acme" }), console, client, A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(["-id", PipelineId, "-j"]);

        await command.Execute();

        var line = Assert.Single(console.Lines);
        Assert.Contains("\"FailureKind\":\"OutcomeUnknown\"", line);
        Assert.Contains("\"FailureKind\":\"Failed\"", line);
        Assert.Contains("\"FailureKind\":null", line);
    }

    private static (GetLatestPipelineExecutionCommand Command, ICommunicationServicesClient Client, RecordingConsole Console)
        NewLatest()
    {
        var client = A.Fake<ICommunicationServicesClient>();
        var console = new RecordingConsole();
        var command = new GetLatestPipelineExecutionCommand(NullLogger<GetLatestPipelineExecutionCommand>.Instance,
            Options.Create(new OctoToolOptions { TenantId = "acme" }), console, client, A.Fake<IAuthenticationService>());
        return (command, client, console);
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
