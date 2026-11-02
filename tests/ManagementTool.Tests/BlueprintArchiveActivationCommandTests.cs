using FakeItEasy;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Blueprints;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Blueprints;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#7991: <c>InstallBlueprint</c> and <c>UpdateBlueprint</c> report what the operation did with the archives of the
///     blueprint seed (activated, skipped with a reason, failed, backfill job of new rollups) and the warnings of the
///     service, instead of dropping them.
/// </summary>
public class BlueprintArchiveActivationCommandTests
{
    private const string Job = "6ae5b33b2a27b45f5568bee8";

    private static BlueprintArchiveActivationDto[] Activations() =>
    [
        new()
        {
            ArchiveRtId = "6a4aa4cc22664f2b26c67e6b", WellKnownName = "GridConnectionRollup1h", IsRollup = true,
            Outcome = "Activated", Backfill = "Queued", BackfillJobRtId = Job
        },
        new() { ArchiveRtId = "6a0e000000000000000a0001", Outcome = "Activated", Backfill = "NotApplicable" },
        new()
        {
            ArchiveRtId = "6a0e000000000000000a0002", WellKnownName = "TwaRollup", IsRollup = true,
            Outcome = "Activated", Backfill = "NotQueued", BackfillReason = "AB#7984: the LOCF carry statement"
        },
        new()
        {
            ArchiveRtId = "6a0e000000000000000a0003", Outcome = "SkippedStreamDataDisabled",
            Reason = "Stream data is not enabled", Backfill = "NotApplicable"
        },
        new()
        {
            ArchiveRtId = "6a0e000000000000000a0004", Outcome = "Failed", Reason = "source not active",
            Backfill = "NotApplicable"
        },
        new() { ArchiveRtId = "6a0e000000000000000a0005", Outcome = "SkippedPreExisting", Backfill = "NotApplicable" }
    ];

    private static string Joined<T>(RecordingLogger<T> logger, LogLevel? level = null) =>
        string.Join("\n", logger.Entries.Where(e => level == null || e.Level == level).Select(e => e.Message));

    [Fact]
    public async Task Install_ReportsTheArchivesTheBackfillJobsAndTheWarnings()
    {
        var client = A.Fake<IAssetServicesClient>();
        A.CallTo(() => client.ApplyBlueprintAsync(A<string>._, A<BlueprintApplyRequestDto>._))
            .Returns(new BlueprintApplyResultDto
            {
                Success = true,
                BlueprintId = "EnergyIQ.Base-1.1.0",
                Warnings = ["Stream data is not enabled on tenant 'acme'"],
                ArchiveActivations = [.. Activations()]
            });
        var lines = new List<string>();
        var console = A.Fake<IConsoleService>();
        A.CallTo(() => console.WriteLine(A<string>._)).Invokes((string line) => lines.Add(line));
        var logger = new RecordingLogger<InstallBlueprint>();
        var command = new InstallBlueprint(logger, console, Options.Create(new OctoToolOptions { TenantId = "acme" }),
            client, A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(["-b", "EnergyIQ.Base-1.1.0"]);

        await command.Execute();

        Assert.Contains("Stream data is not enabled on tenant 'acme'", Joined(logger, LogLevel.Warning));
        Assert.Contains("3 Activated, 1 SkippedStreamDataDisabled, 1 Failed, 1 SkippedPreExisting; 1 rollup backfill(s) queued",
            Joined(logger, LogLevel.Information));
        Assert.Contains($"job {Job}", Joined(logger, LogLevel.Information));
        Assert.Contains("AB#7984", Joined(logger, LogLevel.Warning));
        Assert.Contains("6a0e000000000000000a0003", Joined(logger, LogLevel.Warning));
        Assert.Contains("source not active", Joined(logger, LogLevel.Error));
        // The JSON result on stdout carries the list too.
        Assert.Contains("archiveActivations", string.Join("\n", lines), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Job, string.Join("\n", lines));
    }

    [Fact]
    public async Task Update_ReportsTheArchivesTheBackfillJobsAndTheWarnings()
    {
        var client = A.Fake<IAssetServicesClient>();
        A.CallTo(() => client.ApplyBlueprintUpdateAsync(A<string>._, A<BlueprintUpdateRequestDto>._))
            .Returns(new BlueprintUpdateResultDto
            {
                Success = true,
                Warnings = ["one warning"],
                ArchiveActivations = [.. Activations()]
            });
        var logger = new RecordingLogger<UpdateBlueprint>();
        var command = new UpdateBlueprint(logger, Options.Create(new OctoToolOptions { TenantId = "acme" }), client,
            A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(["-tv", "EnergyIQ.Base-1.1.0"]);

        await command.Execute();

        Assert.Contains("one warning", Joined(logger, LogLevel.Warning));
        Assert.Contains("1 rollup backfill(s) queued", Joined(logger, LogLevel.Information));
        Assert.Contains($"job {Job}", Joined(logger, LogLevel.Information));
    }

    [Fact]
    public async Task Install_WithoutArchives_LogsNoArchiveLine()
    {
        var client = A.Fake<IAssetServicesClient>();
        A.CallTo(() => client.ApplyBlueprintAsync(A<string>._, A<BlueprintApplyRequestDto>._))
            .Returns(new BlueprintApplyResultDto { Success = true, BlueprintId = "Shop-1.0.0" });
        var logger = new RecordingLogger<InstallBlueprint>();
        var command = new InstallBlueprint(logger, A.Fake<IConsoleService>(),
            Options.Create(new OctoToolOptions { TenantId = "acme" }), client, A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(["-b", "Shop-1.0.0"]);

        await command.Execute();

        Assert.DoesNotContain("Archives of the blueprint seed", Joined(logger));
    }

    [Fact]
    public void Summarize_CountsPerOutcome()
    {
        Assert.Equal("3 Activated, 1 SkippedStreamDataDisabled, 1 Failed, 1 SkippedPreExisting; 1 rollup backfill(s) queued",
            BlueprintArchiveActivationReport.Summarize(Activations()));
    }
}
