using FakeItEasy;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Secrets;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.IdentityProviders;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#5543 — CLI surface of the SECRET value type: SecretStatus / ReprotectSecrets over the bot service secret
///     sweep API, and the write-only identity-provider client secret.
/// </summary>
public sealed class SecretCommandsTests
{
    private const string JobId = "6abf74c0466e54c8b0245209";
    private const string ProviderId = "507f1f77bcf86cd799439099";

    private static SecretSweepReportDto Report(string tenantId, bool violation = false, int reEnter = 0) => new()
    {
        TenantId = tenantId,
        Mode = SecretSweepModeDto.Verify,
        Trigger = SecretSweepTriggerDto.Restore,
        Outcome = SecretSweepOutcomeDto.Succeeded,
        ActiveKeyId = "k1",
        StrictModeActive = violation,
        StrictModeViolation = violation,
        RemainingLegacyValues = violation ? 2 : 0,
        Steps =
        [
            new SecretSweepStepReportDto
            {
                Mode = SecretSweepModeDto.Verify,
                Success = true,
                Totals = new SecretFormCountsReportDto
                {
                    Plaintext = violation ? 2 : 0, EncV2 = 4, Total = 6,
                    EncV2ByKeyId = new Dictionary<string, long> { ["k1"] = 4 }
                },
                Slots =
                [
                    new SecretSlotCountsReportDto
                    {
                        CkTypeId = "System.Communication/EMailSenderConfiguration", AttributePath = "Password",
                        Counts = new SecretFormCountsReportDto { EncV2 = 4, Total = 4 }
                    }
                ]
            }
        ],
        SecretsToReEnter = Enumerable.Range(0, reEnter).Select(_ => new SecretValueReferenceDto
        {
            CkTypeId = "System.Communication/SftpConfiguration", RtId = "507f1f77bcf86cd799439011",
            AttributePath = "PrivateKey", PreviousForm = SecretValueFormDto.UnknownKeyId, KeyId = "k0"
        }).ToList()
    };

    // ── SecretStatus ──────────────────────────────────────────────────────

    [Fact]
    public async Task SecretStatus_DefaultsToTheContextTenant_AndPrintsSlotsAndReEntryList()
    {
        var bot = A.Fake<IBotServicesClient>();
        A.CallTo(() => bot.GetSecretSweepReportAsync("acme")).Returns(Report("acme", reEnter: 1));
        var logger = new RecordingLogger<SecretStatusCommand>();
        var command = NewStatus(bot, logger);
        command.CommandArgumentValue.ParseLayer([]);

        await command.Execute();

        Assert.Contains(logger.Entries, e => e.Message.Contains("EMailSenderConfiguration") && e.Message.Contains("Password"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("encV2 key id k1: 4"));
        Assert.Contains(logger.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("1 secret(s) must be re-entered"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("PrivateKey") && e.Message.Contains("k0"));
        A.CallTo(() => bot.GetSecretSweepReportsAsync()).MustNotHaveHappened();
    }

    [Fact]
    public async Task SecretStatus_ExplicitTenant_IsUsed()
    {
        var bot = A.Fake<IBotServicesClient>();
        A.CallTo(() => bot.GetSecretSweepReportAsync("other")).Returns((SecretSweepReportDto?)null);
        var logger = new RecordingLogger<SecretStatusCommand>();
        var command = NewStatus(bot, logger);
        command.CommandArgumentValue.ParseLayer(["-tid", "Other"]);

        await command.Execute();

        A.CallTo(() => bot.GetSecretSweepReportAsync("other")).MustHaveHappenedOnceExactly();
        Assert.Contains(logger.Entries, e => e.Message.Contains("No secret sweep report"));
    }

    [Fact]
    public async Task SecretStatus_All_PrintsOneLinePerTenantAndWarnsOnViolations()
    {
        var bot = A.Fake<IBotServicesClient>();
        A.CallTo(() => bot.GetSecretSweepReportsAsync())
            .Returns(new List<SecretSweepReportDto> { Report("a"), Report("b", violation: true) });
        var logger = new RecordingLogger<SecretStatusCommand>();
        var command = NewStatus(bot, logger);
        command.CommandArgumentValue.ParseLayer(["-a"]);

        await command.Execute();

        Assert.Contains(logger.Entries, e => e.Message.Contains("2 tenant report(s)"));
        Assert.Contains(logger.Entries, e => e.Message.TrimStart().StartsWith("b ") && e.Message.Contains("VIOLATION"));
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("strict mode violation"));
        A.CallTo(() => bot.GetSecretSweepReportAsync(A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task SecretStatus_Json_WritesTheRawReport()
    {
        var bot = A.Fake<IBotServicesClient>();
        A.CallTo(() => bot.GetSecretSweepReportAsync("acme")).Returns(Report("acme"));
        var console = A.Fake<IConsoleService>();
        var command = NewStatus(bot, new RecordingLogger<SecretStatusCommand>(), console: console);
        command.CommandArgumentValue.ParseLayer(["-j"]);

        await command.Execute();

        A.CallTo(() => console.WriteLine(A<string>.That.Matches(json =>
            json.Contains("\"ActiveKeyId\": \"k1\"") && json.Contains("\"environment\"") &&
            json.Contains("\"recentRuns\"")))).MustHaveHappened();
    }

    [Fact]
    public async Task SecretStatus_TenantAndAll_IsRejected()
    {
        var bot = A.Fake<IBotServicesClient>();
        var command = NewStatus(bot, new RecordingLogger<SecretStatusCommand>());
        command.CommandArgumentValue.ParseLayer(["-a", "-tid", "x"]);

        await Assert.ThrowsAsync<ToolException>(command.Execute);
    }

    [Fact]
    public async Task SecretStatus_WithoutTenant_Throws()
    {
        var bot = A.Fake<IBotServicesClient>();
        var command = NewStatus(bot, new RecordingLogger<SecretStatusCommand>(), tenantId: null);
        command.CommandArgumentValue.ParseLayer([]);

        await Assert.ThrowsAsync<ToolException>(command.Execute);
        A.CallTo(() => bot.GetSecretSweepReportAsync(A<string>._)).MustNotHaveHappened();
    }

    // ── ReprotectSecrets ──────────────────────────────────────────────────

    [Fact]
    public async Task ReprotectSecrets_DefaultMode_IsReprotectAndAsksForConfirmation()
    {
        var bot = NewBotWithJob();
        var confirmation = new FakeConfirmationService(true);
        var logger = new RecordingLogger<ReprotectSecretsCommand>();
        var command = NewReprotect(bot, logger, confirmation);
        command.CommandArgumentValue.ParseLayer([]);

        await command.Execute();

        A.CallTo(() => bot.StartSecretSweepAsync("acme", SecretSweepModeDto.Reprotect, true)).MustHaveHappenedOnceExactly();
        Assert.Contains("re-encrypt all secrets of tenant 'acme'", confirmation.LastMessage);
        Assert.Contains(logger.Entries, e => e.Message.Contains(JobId));
        A.CallTo(() => bot.GetImportJobStatus(A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ReprotectSecrets_Declined_NeverStartsTheJob()
    {
        var bot = NewBotWithJob();
        var confirmation = new FakeConfirmationService(false);
        var command = NewReprotect(bot, new RecordingLogger<ReprotectSecretsCommand>(), confirmation);
        command.CommandArgumentValue.ParseLayer(["-m", "Encrypt"]);

        await Assert.ThrowsAsync<ToolException>(command.Execute);

        A.CallTo(() => bot.StartSecretSweepAsync(A<string>._, A<SecretSweepModeDto>._, A<bool>._)).MustNotHaveHappened();
        Assert.Contains("encrypt all remaining plaintext", confirmation.LastMessage);
    }

    [Fact]
    public async Task ReprotectSecrets_Verify_RunsWithoutConfirmation()
    {
        var bot = NewBotWithJob();
        var confirmation = new FakeConfirmationService(false);
        var command = NewReprotect(bot, new RecordingLogger<ReprotectSecretsCommand>(), confirmation);
        command.CommandArgumentValue.ParseLayer(["-m", "verify", "-tid", "Other"]);

        await command.Execute();

        A.CallTo(() => bot.StartSecretSweepAsync("other", SecretSweepModeDto.Verify, false)).MustHaveHappenedOnceExactly();
        Assert.Null(confirmation.LastMessage);
    }

    [Fact]
    public async Task ReprotectSecrets_AllWithYes_UsesTheSystemEndpointWithoutPrompt()
    {
        var bot = NewBotWithJob();
        var confirmation = new FakeConfirmationService(false);
        var command = NewReprotect(bot, new RecordingLogger<ReprotectSecretsCommand>(), confirmation);
        command.CommandArgumentValue.ParseLayer(["-a", "-m", "Encrypt", "-y"]);

        await command.Execute();

        A.CallTo(() => bot.StartSecretSweepAllTenantsAsync(SecretSweepModeDto.Encrypt, true)).MustHaveHappenedOnceExactly();
        A.CallTo(() => bot.StartSecretSweepAsync(A<string>._, A<SecretSweepModeDto>._, A<bool>._)).MustNotHaveHappened();
        Assert.Null(confirmation.LastMessage);
    }

    [Theory]
    [InlineData("Decrypt")]
    [InlineData("4")]
    [InlineData("Wipe")]
    [InlineData("ClearUnknownKid")]
    public async Task ReprotectSecrets_DecryptOrUnknownMode_IsRefused(string mode)
    {
        var bot = NewBotWithJob();
        var command = NewReprotect(bot, new RecordingLogger<ReprotectSecretsCommand>(), new FakeConfirmationService(true));
        command.CommandArgumentValue.ParseLayer(["-m", mode, "-y"]);

        var ex = await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Contains("Verify, Encrypt, Reprotect or CleanupUnreadable", ex.Message);
        A.CallTo(() => bot.StartSecretSweepAsync(A<string>._, A<SecretSweepModeDto>._, A<bool>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ReprotectSecrets_CleanupUnreadableWithoutYes_IsRefusedWithoutPrompt()
    {
        var bot = NewBotWithJob();
        var confirmation = new FakeConfirmationService(true);
        var command = NewReprotect(bot, new RecordingLogger<ReprotectSecretsCommand>(), confirmation);
        command.CommandArgumentValue.ParseLayer(["-m", "CleanupUnreadable"]);

        var ex = await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Contains("-y", ex.Message);
        Assert.Null(confirmation.LastMessage);
        A.CallTo(() => bot.StartSecretSweepAsync(A<string>._, A<SecretSweepModeDto>._, A<bool>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ReprotectSecrets_CleanupUnreadableWithYes_SendsConfirmToTheBot()
    {
        var bot = NewBotWithJob();
        var command = NewReprotect(bot, new RecordingLogger<ReprotectSecretsCommand>(), new FakeConfirmationService(false));
        command.CommandArgumentValue.ParseLayer(["-m", "cleanupunreadable", "-y", "-tid", "acme"]);

        await command.Execute();

        A.CallTo(() => bot.StartSecretSweepAsync("acme", SecretSweepModeDto.CleanupUnreadable, true))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ReprotectSecrets_EncryptConfirmedInteractively_SendsConfirmToTheBot()
    {
        var bot = NewBotWithJob();
        var command = NewReprotect(bot, new RecordingLogger<ReprotectSecretsCommand>(), new FakeConfirmationService(true));
        command.CommandArgumentValue.ParseLayer(["-m", "Encrypt"]);

        await command.Execute();

        A.CallTo(() => bot.StartSecretSweepAsync("acme", SecretSweepModeDto.Encrypt, true)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ReprotectSecrets_Wait_PollsTheJobAndPrintsTheReport()
    {
        var bot = NewBotWithJob();
        A.CallTo(() => bot.GetImportJobStatus(JobId)).Returns(new JobDto { Id = JobId, Status = "Succeeded" });
        A.CallTo(() => bot.GetSecretSweepReportAsync("acme")).Returns(Report("acme"));
        var logger = new RecordingLogger<ReprotectSecretsCommand>();
        var command = NewReprotect(bot, logger, new FakeConfirmationService(true));
        command.CommandArgumentValue.ParseLayer(["-w", "-y"]);

        await command.Execute();

        A.CallTo(() => bot.GetImportJobStatus(JobId)).MustHaveHappenedOnceExactly();
        Assert.Contains(logger.Entries, e => e.Message.Contains("Tenant 'acme': last sweep"));
    }

    // ── SecretStatus: environment status, runs, unreadable list ───────────

    [Fact]
    public async Task SecretStatus_Tenant_PrintsEnvironmentRunsWithDumpStateAndUnreadableList()
    {
        var bot = A.Fake<IBotServicesClient>();
        A.CallTo(() => bot.GetSecretEnvironmentStatusAsync("acme")).Returns(Environment());
        A.CallTo(() => bot.GetSecretSweepRunsAsync("acme", SecretStatusCommand.RecentRunLimit)).Returns(
            new List<SecretSweepRunDto>
            {
                new()
                {
                    RunId = "run-2", Mode = SecretSweepModeDto.CleanupUnreadable,
                    Outcome = SecretSweepOutcomeDto.Succeeded, UnreadableCount = 0,
                    Dump = new SecretSweepDumpDto
                    {
                        FileName = "acme-presweep.tar.gz", Exists = false,
                        DeletedAt = new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc), DeletedBy = "ops"
                    }
                },
                new()
                {
                    RunId = "run-1", Mode = SecretSweepModeDto.Encrypt, Outcome = SecretSweepOutcomeDto.Succeeded,
                    Dump = new SecretSweepDumpDto { FileName = "acme-1-presweep.tar.gz", Exists = true, SizeBytes = 512 }
                }
            });
        var report = Report("acme");
        report.Unreadable.Add(new SecretUnreadableValueDto
        {
            CkTypeId = "System.Communication/SftpConfiguration", RtId = "507f1f77bcf86cd799439012",
            AttributePath = "password", KeyId = "src1"
        });
        report.PlaceholdersNormalized = 2;
        A.CallTo(() => bot.GetSecretSweepReportAsync("acme")).Returns(report);
        var logger = new RecordingLogger<SecretStatusCommand>();
        var command = NewStatus(bot, logger);
        command.CommandArgumentValue.ParseLayer([]);

        await command.Execute();

        Assert.Contains(logger.Entries, e => e.Message.Contains("active key id k2") && e.Message.Contains("k1, k2"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("Strict mode: on (since 2026-10-01"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("0 3 * * *") && e.Message.Contains("2026-10-06 03:00:00Z"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("2 recent sweep run(s)"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("run-2") && e.Message.Contains("deleted") && e.Message.Contains("by ops"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("run-1") && e.Message.Contains("512 bytes"));
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("1 unreadable secret(s)"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("SftpConfiguration") && e.Message.Contains("src1"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("legacy placeholders normalised: 2"));
    }

    [Fact]
    public async Task SecretStatus_KeyRingNotConfigured_Warns()
    {
        var bot = A.Fake<IBotServicesClient>();
        A.CallTo(() => bot.GetSecretEnvironmentStatusAsync("acme"))
            .Returns(new SecretEnvironmentStatusDto { KeyRingConfigured = false });
        A.CallTo(() => bot.GetSecretSweepReportAsync("acme")).Returns((SecretSweepReportDto?)null);
        var logger = new RecordingLogger<SecretStatusCommand>();
        var command = NewStatus(bot, logger);
        command.CommandArgumentValue.ParseLayer([]);

        await command.Execute();

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("NOT configured"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("Sweep runs: none"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("No secret sweep report"));
    }

    // ── DeleteSecretSweepDump ─────────────────────────────────────────────

    [Fact]
    public async Task DeleteSecretSweepDump_WithYes_DeletesWithoutPrompt()
    {
        var bot = A.Fake<IBotServicesClient>();
        A.CallTo(() => bot.DeleteSecretSweepDumpAsync("other", "run-1")).Returns(SecretSweepDumpDeleteResultDto.Deleted);
        var confirmation = new FakeConfirmationService(false);
        var logger = new RecordingLogger<DeleteSecretSweepDumpCommand>();
        var command = NewDeleteDump(bot, logger, confirmation);
        command.CommandArgumentValue.ParseLayer(["-tid", "Other", "-r", "run-1", "-y"]);

        await command.Execute();

        A.CallTo(() => bot.DeleteSecretSweepDumpAsync("other", "run-1")).MustHaveHappenedOnceExactly();
        Assert.Null(confirmation.LastMessage);
        Assert.Contains(logger.Entries, e => e.Message.Contains("deleted"));
    }

    [Fact]
    public async Task DeleteSecretSweepDump_Declined_NeverDeletes()
    {
        var bot = A.Fake<IBotServicesClient>();
        var confirmation = new FakeConfirmationService(false);
        var command = NewDeleteDump(bot, new RecordingLogger<DeleteSecretSweepDumpCommand>(), confirmation);
        command.CommandArgumentValue.ParseLayer(["--runId", "run-1"]);

        await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Contains("tenant 'acme'", confirmation.LastMessage);
        A.CallTo(() => bot.DeleteSecretSweepDumpAsync(A<string>._, A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task DeleteSecretSweepDump_NotFound_Throws()
    {
        var bot = A.Fake<IBotServicesClient>();
        A.CallTo(() => bot.DeleteSecretSweepDumpAsync("acme", "nope")).Returns(SecretSweepDumpDeleteResultDto.NotFound);
        var command = NewDeleteDump(bot, new RecordingLogger<DeleteSecretSweepDumpCommand>(), new FakeConfirmationService(true));
        command.CommandArgumentValue.ParseLayer(["-r", "nope"]);

        var ex = await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Contains("nope", ex.Message);
    }

    [Fact]
    public async Task DeleteSecretSweepDump_AlreadyDeleted_Warns()
    {
        var bot = A.Fake<IBotServicesClient>();
        A.CallTo(() => bot.DeleteSecretSweepDumpAsync("acme", "run-1")).Returns(SecretSweepDumpDeleteResultDto.AlreadyDeleted);
        var logger = new RecordingLogger<DeleteSecretSweepDumpCommand>();
        var command = NewDeleteDump(bot, logger, new FakeConfirmationService(true));
        command.CommandArgumentValue.ParseLayer(["-r", "run-1", "-y"]);

        await command.Execute();

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("already deleted"));
    }

    [Fact]
    public async Task DeleteSecretSweepDump_WithoutTenant_Throws()
    {
        var bot = A.Fake<IBotServicesClient>();
        var command = NewDeleteDump(bot, new RecordingLogger<DeleteSecretSweepDumpCommand>(),
            new FakeConfirmationService(true), tenantId: null);
        command.CommandArgumentValue.ParseLayer(["-r", "run-1", "-y"]);

        await Assert.ThrowsAsync<ToolException>(command.Execute);
        A.CallTo(() => bot.DeleteSecretSweepDumpAsync(A<string>._, A<string>._)).MustNotHaveHappened();
    }

    // ── UpdateIdentityProvider (write-only client secret) ─────────────────

    [Fact]
    public async Task UpdateIdentityProvider_WithoutSecret_SendsNullAndKeepsClientId()
    {
        var identity = A.Fake<IIdentityServicesClient>();
        A.CallTo(() => identity.GetIdentityProvider(A<OctoObjectId>._)).Returns(new AzureEntraIdProviderDto
        {
            Name = "Old", TenantId = "azure-tid", Authority = "https://authority/", ClientId = "cid",
            ClientSecretIsSet = true
        });
        IdentityProviderDto? sent = null;
        A.CallTo(() => identity.UpdateIdentityProvider(A<OctoObjectId>._, A<IdentityProviderDto>._))
            .Invokes((OctoObjectId _, IdentityProviderDto dto) => sent = dto);
        var command = new UpdateIdentityProvider(new RecordingLogger<UpdateIdentityProvider>(),
            Options.Create(new OctoToolOptions { TenantId = "acme" }), identity, A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(["-id", ProviderId, "-n", "New", "-e", "true"]);

        await command.Execute();

        var azure = Assert.IsType<AzureEntraIdProviderDto>(sent);
        Assert.Null(azure.ClientSecret);
        Assert.Equal("cid", azure.ClientId);
        Assert.Equal("azure-tid", azure.TenantId);
    }

    [Fact]
    public async Task UpdateIdentityProvider_Google_EchoedSecretIsNeverSentBack()
    {
        var identity = A.Fake<IIdentityServicesClient>();
        A.CallTo(() => identity.GetIdentityProvider(A<OctoObjectId>._)).Returns(new GoogleIdentityProviderDto
        {
            Name = "Old", ClientId = "cid", ClientSecret = "echoed-by-an-old-server"
        });
        IdentityProviderDto? sent = null;
        A.CallTo(() => identity.UpdateIdentityProvider(A<OctoObjectId>._, A<IdentityProviderDto>._))
            .Invokes((OctoObjectId _, IdentityProviderDto dto) => sent = dto);
        var command = new UpdateIdentityProvider(new RecordingLogger<UpdateIdentityProvider>(),
            Options.Create(new OctoToolOptions { TenantId = "acme" }), identity, A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(["-id", ProviderId, "-n", "New", "-e", "true"]);

        await command.Execute();

        var google = Assert.IsType<GoogleIdentityProviderDto>(sent);
        Assert.Null(google.ClientSecret);
        Assert.Equal("cid", google.ClientId);
    }

    [Fact]
    public async Task UpdateIdentityProvider_WithNewSecret_RotatesIt()
    {
        var identity = A.Fake<IIdentityServicesClient>();
        A.CallTo(() => identity.GetIdentityProvider(A<OctoObjectId>._))
            .Returns(new MicrosoftIdentityProviderDto { Name = "Old", ClientId = "cid" });
        IdentityProviderDto? sent = null;
        A.CallTo(() => identity.UpdateIdentityProvider(A<OctoObjectId>._, A<IdentityProviderDto>._))
            .Invokes((OctoObjectId _, IdentityProviderDto dto) => sent = dto);
        var command = new UpdateIdentityProvider(new RecordingLogger<UpdateIdentityProvider>(),
            Options.Create(new OctoToolOptions { TenantId = "acme" }), identity, A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(["-id", ProviderId, "-n", "New", "-e", "true", "-cs", "rotated-test-secret"]);

        await command.Execute();

        Assert.Equal("rotated-test-secret", Assert.IsType<MicrosoftIdentityProviderDto>(sent).ClientSecret);
    }

    [Fact]
    public async Task GetIdentityProviders_ClientSecretFromOlderService_IsNotPrinted()
    {
        const string fakeSecret = "fake-secret-echoed-by-an-old-server";
        var identity = A.Fake<IIdentityServicesClient>();
        A.CallTo(() => identity.GetIdentityProviders()).Returns(new List<IdentityProviderDto>
        {
            new GoogleIdentityProviderDto { Name = "G", ClientId = "g", ClientSecret = fakeSecret },
            new MicrosoftIdentityProviderDto { Name = "M", ClientId = "m", ClientSecret = fakeSecret },
            new FacebookIdentityProviderDto { Name = "F", ClientId = "f", ClientSecret = fakeSecret },
            new AzureEntraIdProviderDto
            {
                Name = "A", ClientId = "a", TenantId = "t", ClientSecret = fakeSecret, ClientSecretIsSet = true
            }
        });
        var console = A.Fake<IConsoleService>();
        var printed = new List<string>();
        A.CallTo(() => console.WriteLine(A<string>._)).Invokes((string line) => printed.Add(line));
        var command = new GetIdentityProviders(new RecordingLogger<GetIdentityProviders>(),
            Options.Create(new OctoToolOptions { TenantId = "acme" }), console, identity,
            A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer([]);

        await command.Execute();

        var output = string.Join("\n", printed);
        Assert.Contains("clientSecretIsSet", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fakeSecret, output);
    }

    private static IBotServicesClient NewBotWithJob()
    {
        var bot = A.Fake<IBotServicesClient>();
        A.CallTo(() => bot.StartSecretSweepAsync(A<string>._, A<SecretSweepModeDto>._, A<bool>._)).Returns(new JobResponseDto(JobId));
        A.CallTo(() => bot.StartSecretSweepAllTenantsAsync(A<SecretSweepModeDto>._, A<bool>._)).Returns(new JobResponseDto(JobId));
        return bot;
    }

    private static SecretEnvironmentStatusDto Environment() => new()
    {
        KeyRingConfigured = true,
        ActiveKeyId = "k2",
        KnownKeyIds = ["k1", "k2"],
        LegacyV1KeyConfigured = true,
        StrictMode = true,
        StrictModeSince = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        RecurringVerifyCron = "0 3 * * *",
        LastVerifyAt = new DateTime(2026, 10, 6, 3, 0, 0, DateTimeKind.Utc)
    };

    private static DeleteSecretSweepDumpCommand NewDeleteDump(IBotServicesClient bot,
        ILogger<DeleteSecretSweepDumpCommand> logger, IConfirmationService confirmation, string? tenantId = "acme") =>
        new(logger, Options.Create(new OctoToolOptions { TenantId = tenantId }), bot,
            A.Fake<IAuthenticationService>(), confirmation);

    private static SecretStatusCommand NewStatus(IBotServicesClient bot, ILogger<SecretStatusCommand> logger,
        string? tenantId = "acme", IConsoleService? console = null) =>
        new(logger, Options.Create(new OctoToolOptions { TenantId = tenantId }), bot,
            A.Fake<IAuthenticationService>(), console ?? A.Fake<IConsoleService>());

    private static ReprotectSecretsCommand NewReprotect(IBotServicesClient bot, ILogger<ReprotectSecretsCommand> logger,
        IConfirmationService confirmation, string? tenantId = "acme") =>
        new(logger, Options.Create(new OctoToolOptions { TenantId = tenantId }), bot,
            A.Fake<IAuthenticationService>(), confirmation);

    private sealed class FakeConfirmationService(bool answer) : IConfirmationService
    {
        public string? LastMessage { get; private set; }

        public bool Confirm(string message)
        {
            LastMessage = message;
            return answer;
        }
    }
}
