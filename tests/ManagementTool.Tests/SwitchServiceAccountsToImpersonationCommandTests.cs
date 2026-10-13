using FakeItEasy;
using System.Net;
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
using Newtonsoft.Json;

namespace ManagementTool.Tests;

/// <summary>
/// AB#6642 — the CLI surface of the impersonation switch (AB#5537). Pinned: dry run is the default and
/// never prompts, apply is confirmation-gated, a 409 becomes a ToolException, Failed accounts and failed
/// redeploys make the exit non-zero, SwitchedPendingInvalidation only warns, and nothing secret is printed.
/// </summary>
public sealed class SwitchServiceAccountsToImpersonationCommandTests
{
    private static ServiceAccountImpersonationSwitchEntryDto Entry(string outcome, string name = "sa-1") =>
        new("68c1a2b3c4d5e6f701020304", name, "octo-" + name, outcome, "msg " + outcome, ["p1"]);

    private static ServiceAccountImpersonationSwitchReportDto Report(bool applied,
        IReadOnlyList<ServiceAccountImpersonationSwitchEntryDto> accounts,
        IReadOnlyList<DataFlowRedeployFailureDto>? failures = null) =>
        new("acme", applied, accounts, ["df1"], failures ?? []);

    private sealed class Rig
    {
        public Rig(ServiceAccountImpersonationSwitchReportDto? report = null, bool confirm = true,
            string? tenantId = "acme", Exception? throws = null)
        {
            Client = A.Fake<ICommunicationServicesClient>();
            A.CallTo(() => Client.ServiceUri).Returns(new Uri("https://comm.example.com/acme/v1"));
            var call = A.CallTo(() => Client.SwitchServiceAccountsToImpersonationAsync(A<bool>._, A<bool>._));
            if (throws != null)
            {
                call.Throws(throws);
            }
            else
            {
                call.Returns(Task.FromResult(report ?? Report(false, [Entry("WouldSwitch")])));
            }

            Confirmation = new FakeConfirmationService(confirm);
            Console = new RecordingConsoleService();
            Logger = new RecordingLogger<SwitchServiceAccountsToImpersonationCommand>();
            Command = new SwitchServiceAccountsToImpersonationCommand(Logger,
                Options.Create(new OctoToolOptions { TenantId = tenantId }), Console, Client,
                new FakeAuthenticationService(), Confirmation);
        }

        public ICommunicationServicesClient Client { get; }
        public FakeConfirmationService Confirmation { get; }
        public RecordingConsoleService Console { get; }
        public RecordingLogger<SwitchServiceAccountsToImpersonationCommand> Logger { get; }
        public SwitchServiceAccountsToImpersonationCommand Command { get; }

        public Task Run(params string[] args)
        {
            Command.CommandArgumentValue.ParseLayer(args);
            return Command.Execute();
        }

        public void Called(bool apply, bool redeploy) =>
            A.CallTo(() => Client.SwitchServiceAccountsToImpersonationAsync(apply, redeploy))
                .MustHaveHappenedOnceExactly();

        public void NeverCalled() =>
            A.CallTo(() => Client.SwitchServiceAccountsToImpersonationAsync(A<bool>._, A<bool>._))
                .MustNotHaveHappened();
    }

    [Fact]
    public async Task NoArguments_IsADryRun_WithoutPrompt_ListsAccountsAndCounts()
    {
        var rig = new Rig(Report(false, [Entry("WouldSwitch", "a"), Entry("WouldSwitch", "b"), Entry("SkippedIncomplete", "c")]));

        await rig.Run();

        rig.Called(apply: false, redeploy: true);
        Assert.Null(rig.Confirmation.LastMessage);
        Assert.Contains(rig.Console.Lines, l => l.Contains("DRY RUN") && l.Contains("nothing was changed"));
        Assert.Contains(rig.Console.Lines, l => l.Contains("a") && l.Contains("octo-a") && l.Contains("WouldSwitch"));
        Assert.Contains(rig.Console.Lines, l => l.Contains("Summary:") && l.Contains("WouldSwitch=2") &&
                                                 l.Contains("SkippedIncomplete=1"));
        Assert.DoesNotContain(rig.Logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Apply_Declined_NeverCallsTheService()
    {
        var rig = new Rig(confirm: false);

        await Assert.ThrowsAsync<ToolException>(() => rig.Run("-a"));

        rig.NeverCalled();
        Assert.Contains("Stored client secrets are cleared", rig.Confirmation.LastMessage);
        Assert.Contains("old secrets are invalidated", rig.Confirmation.LastMessage);
    }

    [Fact]
    public async Task Apply_WithYes_SkipsPromptAndSendsApplyTrue()
    {
        var rig = new Rig(Report(true, [Entry("Switched")]), confirm: false);

        await rig.Run("-a", "-y");

        rig.Called(apply: true, redeploy: true);
        Assert.Null(rig.Confirmation.LastMessage);
        Assert.Contains(rig.Console.Lines, l => l.Contains("APPLIED"));
    }

    [Fact]
    public async Task Apply_Confirmed_Runs()
    {
        var rig = new Rig(Report(true, [Entry("Switched")]));

        await rig.Run("--apply");

        rig.Called(apply: true, redeploy: true);
        Assert.NotNull(rig.Confirmation.LastMessage);
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public async Task RedeployDataFlows_ValueIsPassedThrough(string value, bool expected)
    {
        var rig = new Rig();

        await rig.Run("-rdf", value);

        rig.Called(apply: false, redeploy: expected);
    }

    [Fact]
    public async Task Conflict_BecomesAToolExceptionWithAClearMessage()
    {
        var rig = new Rig(throws: new ServiceClientResultException("nope", HttpStatusCode.Conflict, null));

        var ex = await Assert.ThrowsAsync<ToolException>(() => rig.Run("-a", "-y"));

        Assert.Contains("ALLOWSECRETCLEARING", ex.Message);
        Assert.Contains("dry run", ex.Message);
    }

    [Fact]
    public async Task OtherServerErrors_KeepTheGenericBehaviour()
    {
        var rig = new Rig(throws: new ServiceClientResultException("boom", HttpStatusCode.InternalServerError, null));

        await Assert.ThrowsAsync<ServiceClientResultException>(() => rig.Run("-a", "-y"));
    }

    [Fact]
    public async Task FailedAccount_PrintsReportFirst_ThenWarnsAndThrows()
    {
        var rig = new Rig(Report(true, [Entry("Switched", "ok"), Entry("Failed", "bad")]));

        await Assert.ThrowsAsync<ToolException>(() => rig.Run("-a", "-y"));

        Assert.Contains(rig.Console.Lines, l => l.Contains("bad") && l.Contains("Failed"));
        Assert.Contains(rig.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("bad"));
    }

    [Fact]
    public async Task FailedRedeploy_ListsItAndThrows()
    {
        var rig = new Rig(Report(true, [Entry("Switched")], [new DataFlowRedeployFailureDto("df9", "boom")]));

        await Assert.ThrowsAsync<ToolException>(() => rig.Run("-a", "-y"));

        Assert.Contains(rig.Console.Lines, l => l.Contains("df9") && l.Contains("FAILED"));
        Assert.Contains(rig.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("df9"));
    }

    [Fact]
    public async Task PendingInvalidation_OnlyWarns_AndDoesNotThrow()
    {
        var rig = new Rig(Report(true, [Entry("SwitchedPendingInvalidation")]));

        await rig.Run("-a", "-y", "-rdf", "false");

        var warning = Assert.Single(rig.Logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("--redeployDataFlows true", warning.Message);
    }

    [Fact]
    public async Task Json_PrintsTheWholeReport_AndKeepsTheExitCodes()
    {
        var ok = new Rig(Report(false, [Entry("WouldSwitch")]));
        await ok.Run("-j");
        var line = Assert.Single(ok.Console.Lines);
        var roundTrip = JsonConvert.DeserializeObject<ServiceAccountImpersonationSwitchReportDto>(line);
        Assert.Equal("acme", roundTrip!.TenantId);
        Assert.Equal("WouldSwitch", roundTrip.Accounts[0].Outcome);

        var failed = new Rig(Report(true, [Entry("Failed")]));
        await Assert.ThrowsAsync<ToolException>(() => failed.Run("-a", "-y", "-j"));
    }

    [Fact]
    public async Task MissingTenant_ThrowsNoTenantIdConfigured_WithoutCallingTheService()
    {
        var rig = new Rig(tenantId: null);

        var ex = await Assert.ThrowsAsync<ToolException>(() => rig.Run());

        Assert.Contains("No tenant id", ex.Message);
        rig.NeverCalled();
    }

    [Fact]
    public async Task Output_ContainsOnlyReportFields_NoSecretsOrTokens()
    {
        // Every field of the report populated with a recognisable marker; the printed table and the
        // JSON may contain these markers and nothing else that looks like a credential.
        var report = new ServiceAccountImpersonationSwitchReportDto("tenant-MARK", true,
            [new("cfg-MARK", "wk-MARK", "client-MARK", "Switched", "message-MARK", ["pipe-MARK"])],
            ["df-MARK"], [new("dfFail-MARK", "reason-MARK")]);

        var table = new Rig(report);
        await Assert.ThrowsAsync<ToolException>(() => table.Run("-a", "-y"));
        var json = new Rig(report);
        await Assert.ThrowsAsync<ToolException>(() => json.Run("-a", "-y", "-j"));

        var printed = string.Join('\n', table.Console.Lines.Concat(json.Console.Lines)
            .Concat(table.Logger.Entries.Select(e => e.Message)));
        foreach (var forbidden in new[] { "secret", "password", "token", "bearer" })
        {
            Assert.DoesNotContain(forbidden, printed, StringComparison.OrdinalIgnoreCase);
        }

        // The DTO pins the same from the other side: no secret-bearing member can be printed.
        var members = new[]
            {
                typeof(ServiceAccountImpersonationSwitchReportDto), typeof(ServiceAccountImpersonationSwitchEntryDto),
                typeof(DataFlowRedeployFailureDto)
            }
            .SelectMany(t => t.GetProperties()).Select(p => p.Name);
        Assert.DoesNotContain(members, n => n.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
                                            n.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }

    internal sealed class RecordingConsoleService : IConsoleService
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

    internal sealed class FakeConfirmationService(bool answer) : IConfirmationService
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
}
