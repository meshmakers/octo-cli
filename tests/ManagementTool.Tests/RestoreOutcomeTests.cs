using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     <c>Restore -w</c> must not report a plain "restored" when the finished job lists failed archives or rollups that
///     could not be checked, and must end non-zero (AB#6969).
/// </summary>
public sealed class RestoreOutcomeTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".tar.gz");

    public RestoreOutcomeTests() => File.WriteAllText(_file, "payload");

    public void Dispose() => File.Delete(_file);

    [Fact]
    public async Task Restore_Wait_FailedArchives_ThrowsNamingTheArchivesAndLogsNoPlainRestored()
    {
        var job = new JobDto
        {
            Id = "job-1",
            Status = "Succeeded",
            Outcome = "SucceededWithWarnings",
            FailedArchives =
            [
                new JobFailedArchiveDto("6700000000000000000000b1", "copy failed"),
                new JobFailedArchiveDto("6700000000000000000000b2", null)
            ],
            NotCheckedRollups =
            [
                new JobNotCheckedRollupDto("6700000000000000000000a1", "ArchiveFailed", "its archive failed"),
                new JobNotCheckedRollupDto("6700000000000000000000a2", "NeverActivated", "never activated", false)
            ]
        };
        var (command, logger) = NewRestore(job);

        var exception = await Assert.ThrowsAsync<ToolException>(() => command.Execute());

        Assert.Contains("restored with 2 failed archive(s): 6700000000000000000000b1, 6700000000000000000000b2",
            exception.Message);
        Assert.Contains("1 rollup(s) not checked: 6700000000000000000000a1 [ArchiveFailed]", exception.Message);
        Assert.DoesNotContain("6700000000000000000000a2", exception.Message);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("copy failed"));
        Assert.Contains(logger.Entries,
            e => e.Level == LogLevel.Information && e.Message.Contains("not checked (info)") &&
                 e.Message.Contains("6700000000000000000000a2"));
        Assert.DoesNotContain(logger.Entries, e => e.Message.EndsWith(" restored", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Restore_Wait_BlockingNotCheckedRollupOnly_ThrowsRestoredWithWarnings()
    {
        var job = new JobDto
        {
            Status = "Succeeded",
            NotCheckedRollups = [new JobNotCheckedRollupDto("6700000000000000000000a1", "NoLiveTable", "no table")]
        };
        var (command, _) = NewRestore(job);

        var exception = await Assert.ThrowsAsync<ToolException>(() => command.Execute());

        Assert.Contains("restored with warnings", exception.Message);
        Assert.Contains("6700000000000000000000a1 [NoLiveTable]", exception.Message);
    }

    [Fact]
    public async Task Restore_Wait_OnlyInformationalNotChecked_IsRestoredAndListsThem()
    {
        var job = new JobDto
        {
            Status = "Succeeded",
            Outcome = "Succeeded",
            NotCheckedRollups = [new JobNotCheckedRollupDto("6700000000000000000000a2", "NeverActivated", "never", false)]
        };
        var (command, logger) = NewRestore(job);

        await command.Execute();

        Assert.Contains(logger.Entries, e => e.Message.EndsWith(" restored", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, e => e.Message.Contains("not checked (info)"));
    }

    [Fact]
    public async Task Restore_Wait_OlderBotWithoutOutcome_IsRestored()
    {
        var (command, logger) = NewRestore(new JobDto { Status = "Succeeded" });

        await command.Execute();

        Assert.Contains(logger.Entries, e => e.Message.EndsWith(" restored", StringComparison.Ordinal));
    }

    private (RestoreTenant Command, RecordingLogger<RestoreTenant> Logger) NewRestore(JobDto finishedJob)
    {
        var bot = A.Fake<IBotServicesClient>();
        A.CallTo(() => bot.RestoreRepositoryWithTusAsync(A<string>._, A<string>._, A<string>._, A<string?>._,
                A<bool>._, A<Action<double>?>._, A<CancellationToken>._))
            .Returns(new JobResponseDto("job-1"));
        A.CallTo(() => bot.GetImportJobStatus("job-1")).Returns(finishedJob);
        var logger = new RecordingLogger<RestoreTenant>();
        var command = new RestoreTenant(logger, Options.Create(new OctoToolOptions { TenantId = "acme" }), bot,
            A.Fake<IAuthenticationService>(), Options.Create(new BotServiceClientOptions()));
        command.CommandArgumentValue.ParseLayer(["-tid", "acme", "-db", "db", "-f", _file, "-w"]);
        return (command, logger);
    }
}
