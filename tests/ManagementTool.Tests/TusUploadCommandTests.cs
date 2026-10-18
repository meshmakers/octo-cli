using System.Net;
using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#6970 / AB#6786 — Restore and ImportArchiveData upload in parts through the SDK: the part size option, the
///     progress output for a multi-GB upload and the report of a refused part.
/// </summary>
public sealed class TusUploadCommandTests : IDisposable
{
    private const long MiB = 1024 * 1024;
    private readonly string _file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".tar.gz");
    private readonly string _zip = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".zip");

    public TusUploadCommandTests()
    {
        File.WriteAllText(_file, "payload");
        File.WriteAllText(_zip, "payload");
    }

    public void Dispose()
    {
        File.Delete(_file);
        File.Delete(_zip);
    }

    // ── Part size option ───────────────────────────────────────────────────

    [Fact]
    public async Task Restore_WithoutPartSizeOption_LeavesTheSdkDefaultOf50MiB()
    {
        var (command, _, botOptions) = NewRestore();
        command.CommandArgumentValue.ParseLayer(["-tid", "acme", "-db", "db", "-f", _file]);

        await command.Execute();

        Assert.Equal(50 * MiB, botOptions.Value.TusPartSizeBytes);
    }

    [Fact]
    public async Task Restore_WithPartSizeOption_SetsTheSdkOptionBeforeTheUpload()
    {
        var (command, bot, botOptions) = NewRestore();
        long partSizeAtUpload = 0;
        A.CallTo(() => bot.RestoreRepositoryWithTusAsync(A<string>._, A<string>._, A<string>._, A<string?>._,
                A<bool>._, A<Action<double>?>._, A<CancellationToken>._))
            .Invokes(() => partSizeAtUpload = botOptions.Value.TusPartSizeBytes)
            .Returns(new JobResponseDto("job-1"));
        command.CommandArgumentValue.ParseLayer(["-tid", "acme", "-db", "db", "-f", _file, "-ps", "20"]);

        await command.Execute();

        Assert.Equal(20 * MiB, partSizeAtUpload);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("91")]
    [InlineData("100")]
    public async Task Restore_WithPartSizeOutsideTheRange_FailsBeforeAnythingIsUploaded(string mib)
    {
        var (command, bot, botOptions) = NewRestore();
        command.CommandArgumentValue.ParseLayer(["-tid", "acme", "-db", "db", "-f", _file, "-ps", mib]);

        var exception = await Assert.ThrowsAsync<ToolException>(() => command.Execute());

        Assert.Contains("1 to 90 MiB", exception.Message);
        Assert.Contains("100 MiB", exception.Message);
        Assert.Equal(50 * MiB, botOptions.Value.TusPartSizeBytes);
        A.CallTo(bot).Where(c => c.Method.Name == nameof(IBotServicesClient.RestoreRepositoryWithTusAsync))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task ImportArchiveData_WithPartSizeOption_SetsTheSdkOption()
    {
        var (command, _, botOptions) = NewImport();
        command.CommandArgumentValue.ParseLayer(["-tid", "acme", "-aid", "a1", "-i", _zip, "-ps", "10"]);

        await command.Execute();

        Assert.Equal(10 * MiB, botOptions.Value.TusPartSizeBytes);
    }

    // ── A refused upload is reported, no job is started ────────────────────

    [Fact]
    public async Task Restore_RefusedPart_ThrowsTheUploadExceptionWithStatusPartAndOffset()
    {
        var (command, bot, _) = NewRestore();
        A.CallTo(() => bot.RestoreRepositoryWithTusAsync(A<string>._, A<string>._, A<string>._, A<string?>._,
                A<bool>._, A<Action<double>?>._, A<CancellationToken>._))
            .Throws(new TusUploadException("Upload of part 3 of 70 (offset 104857600) was refused: 413.",
                HttpStatusCode.RequestEntityTooLarge, 3, 70, 104857600, 52428800));
        command.CommandArgumentValue.ParseLayer(["-tid", "acme", "-db", "db", "-f", _file, "-w"]);

        var exception = await Assert.ThrowsAsync<TusUploadException>(() => command.Execute());

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, exception.StatusCode);
        Assert.Equal(3, exception.PartNumber);
        Assert.Equal(104857600, exception.Offset);
        // No job was started or waited for, so no later "empty file" error.
        A.CallTo(() => bot.GetImportJobStatus(A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ImportArchiveData_RefusedPart_ThrowsTheUploadException()
    {
        var (command, bot, _) = NewImport();
        A.CallTo(() => bot.StartImportArchiveDataWithTusAsync(A<string>._, A<string>._, A<string>._,
                A<ArchiveImportMode>._, A<Action<double>?>._, A<CancellationToken>._))
            .Throws(new TusUploadException("refused", HttpStatusCode.RequestEntityTooLarge, 1, 2, 0, 52428800));
        command.CommandArgumentValue.ParseLayer(["-tid", "acme", "-aid", "a1", "-i", _zip]);

        await Assert.ThrowsAsync<TusUploadException>(() => command.Execute());
    }

    // ── Progress output ────────────────────────────────────────────────────

    [Fact]
    public async Task Restore_PassesTheProgressOfTheUploadToTheLog()
    {
        var (command, bot, _, logger) = NewRestoreWithLogger();
        A.CallTo(() => bot.RestoreRepositoryWithTusAsync(A<string>._, A<string>._, A<string>._, A<string?>._,
                A<bool>._, A<Action<double>?>._, A<CancellationToken>._))
            .Invokes((string _, string _, string _, string? _, bool _, Action<double>? progress, CancellationToken _) =>
            {
                progress?.Invoke(0.5);
                progress?.Invoke(1);
            })
            .Returns(new JobResponseDto("job-1"));
        command.CommandArgumentValue.ParseLayer(["-tid", "acme", "-db", "db", "-f", _file]);

        await command.Execute();

        Assert.Contains(logger.Entries, e => e.Message.StartsWith("Upload progress: 50%"));
        Assert.Contains(logger.Entries, e => e.Message.StartsWith("Upload progress: 100%"));
    }

    [Fact]
    public void ProgressLogger_ThousandsOfCallbacks_LogAtMostEveryFivePercent()
    {
        var logger = new RecordingLogger<UploadProgressLogger>();
        var progress = new UploadProgressLogger(logger, 3_476_972_984, () => TimeSpan.FromSeconds(1));

        for (var i = 0; i <= 3000; i++)
        {
            progress.Report(i / 3000d);
        }

        Assert.InRange(logger.Entries.Count, 15, 21);
        Assert.Equal(1, logger.Entries.Count(e => e.Message.StartsWith("Upload progress: 100%")));
        Assert.Contains("3.24 GiB", logger.Entries[^1].Message);
    }

    [Fact]
    public void ProgressLogger_SlowUploadWithoutPercentStep_LogsAfter30Seconds()
    {
        var logger = new RecordingLogger<UploadProgressLogger>();
        var now = TimeSpan.Zero;
        var progress = new UploadProgressLogger(logger, 10_000 * MiB, () => now);

        progress.Report(0.01);
        Assert.Empty(logger.Entries);

        now = TimeSpan.FromSeconds(31);
        progress.Report(0.011);
        Assert.Single(logger.Entries);

        now = TimeSpan.FromSeconds(40);
        progress.Report(0.012);
        Assert.Single(logger.Entries);
    }

    [Fact]
    public void FormatSize_UsesBinaryUnits()
    {
        Assert.Equal("512 B", TusUploadSupport.FormatSize(512));
        Assert.Equal("1 MiB", TusUploadSupport.FormatSize(MiB));
        Assert.Equal("1.5 GiB", TusUploadSupport.FormatSize(1536 * MiB));
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static (RestoreTenant Command, IBotServicesClient Bot, IOptions<BotServiceClientOptions> Options) NewRestore()
    {
        var (command, bot, options, _) = NewRestoreWithLogger();
        return (command, bot, options);
    }

    private static (RestoreTenant Command, IBotServicesClient Bot, IOptions<BotServiceClientOptions> Options,
        RecordingLogger<RestoreTenant> Logger) NewRestoreWithLogger()
    {
        var bot = FakeBot();
        var options = Options.Create(new BotServiceClientOptions());
        var logger = new RecordingLogger<RestoreTenant>();
        var command = new RestoreTenant(logger, Options.Create(new OctoToolOptions { TenantId = "acme" }), bot,
            A.Fake<IAuthenticationService>(), options);
        return (command, bot, options, logger);
    }

    private static (ImportArchiveData Command, IBotServicesClient Bot, IOptions<BotServiceClientOptions> Options) NewImport()
    {
        var bot = FakeBot();
        var options = Options.Create(new BotServiceClientOptions());
        var command = new ImportArchiveData(new RecordingLogger<ImportArchiveData>(),
            Options.Create(new OctoToolOptions { TenantId = "acme" }), bot, A.Fake<IAuthenticationService>(), options);
        return (command, bot, options);
    }

    private static IBotServicesClient FakeBot()
    {
        var bot = A.Fake<IBotServicesClient>();
        A.CallTo(() => bot.RestoreRepositoryWithTusAsync(A<string>._, A<string>._, A<string>._, A<string?>._,
                A<bool>._, A<Action<double>?>._, A<CancellationToken>._))
            .Returns(new JobResponseDto("job-1"));
        A.CallTo(() => bot.StartImportArchiveDataWithTusAsync(A<string>._, A<string>._, A<string>._,
                A<ArchiveImportMode>._, A<Action<double>?>._, A<CancellationToken>._))
            .Returns(new JobResponseDto("job-1"));
        return bot;
    }
}
