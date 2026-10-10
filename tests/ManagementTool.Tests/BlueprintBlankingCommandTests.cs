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
///     AB#6316: PreviewBlueprintUpdate lists the tenant values a blueprint update would blank (summaries
///     only), UpdateBlueprint keeps them unless they are confirmed explicitly, and the confirmation
///     arguments reach the service exactly as typed. Server contract: AB#6315.
/// </summary>
public sealed class BlueprintBlankingCommandTests
{
    private const string RtId1 = "65a1b2c3d4e5f60718293a4b";
    private const string RtId2 = "65a1b2c3d4e5f60718293a4c";

    // ---------- argument parsing ----------

    [Theory]
    [InlineData("65a1b2c3d4e5f60718293a4b:configuration", "65a1b2c3d4e5f60718293a4b", "configuration")]
    [InlineData(" 65a1b2c3d4e5f60718293a4b : password ", "65a1b2c3d4e5f60718293a4b", "password")]
    [InlineData("65a1b2c3d4e5f60718293a4b:a:b", "65a1b2c3d4e5f60718293a4b", "a:b")]
    public void ParseConfirmation_SplitsAtTheFirstColon(string value, string rtId, string attribute)
    {
        var confirmation = BlueprintBlanking.ParseConfirmation(value);

        Assert.Equal(rtId, confirmation.RtId);
        Assert.Equal(attribute, confirmation.AttributeName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("configuration")]
    [InlineData(":configuration")]
    [InlineData("65a1b2c3d4e5f60718293a4b:")]
    [InlineData("65a1b2c3d4e5f60718293a4b:  ")]
    public void ParseConfirmation_RefusesAHalfEmptyConfirmation(string value)
    {
        var exception = Assert.Throws<ToolException>(() => BlueprintBlanking.ParseConfirmation(value));

        Assert.Contains("--confirm-blanking", exception.Message);
        Assert.Contains("<rtId>:<attribute>", exception.Message);
    }

    [Fact]
    public async Task UpdateBlueprint_WithoutBlankingOptions_SendsTheSafeDefault()
    {
        var (client, requests) = NewApplyClient(new BlueprintUpdateResultDto { Success = true });
        var command = NewUpdate(client, new RecordingLogger<UpdateBlueprint>());
        command.CommandArgumentValue.ParseLayer(["-tv", "Eda-1.2.0"]);

        await command.Execute();

        var request = Assert.Single(requests);
        Assert.Equal("Eda-1.2.0", request.TargetVersion);
        Assert.Equal("Merge", request.UpdateMode);
        Assert.False(request.AllowBlanking);
        Assert.Null(request.ConfirmedBlankings);
    }

    [Fact]
    public async Task UpdateBlueprint_RepeatedConfirmBlanking_SendsEveryPair()
    {
        var (client, requests) = NewApplyClient(new BlueprintUpdateResultDto { Success = true });
        var command = NewUpdate(client, new RecordingLogger<UpdateBlueprint>());
        command.CommandArgumentValue.ParseLayer(
            ["-tv", "Eda-1.2.0", "--confirm-blanking", $"{RtId1}:configuration", "-cb", $"{RtId2}:password"]);

        await command.Execute();

        var request = Assert.Single(requests);
        Assert.False(request.AllowBlanking);
        Assert.Equal([(RtId1, "configuration"), (RtId2, "password")],
            request.ConfirmedBlankings!.Select(c => (c.RtId, c.AttributeName)));
    }

    [Fact]
    public async Task UpdateBlueprint_DuplicatePairs_AreSentOnce()
    {
        var (client, requests) = NewApplyClient(new BlueprintUpdateResultDto { Success = true });
        var command = NewUpdate(client, new RecordingLogger<UpdateBlueprint>());
        command.CommandArgumentValue.ParseLayer(
            ["-tv", "v-1", "-cb", $"{RtId1}:Configuration", "-cb", $"{RtId1}:configuration"]);

        await command.Execute();

        Assert.Single(Assert.Single(requests).ConfirmedBlankings!);
    }

    [Fact]
    public async Task UpdateBlueprint_AllowBlanking_SendsTheFlagAndWarnsLoudly()
    {
        var (client, requests) = NewApplyClient(new BlueprintUpdateResultDto { Success = true });
        var logger = new RecordingLogger<UpdateBlueprint>();
        var command = NewUpdate(client, logger);
        command.CommandArgumentValue.ParseLayer(["-tv", "Eda-1.2.0", "--allow-blanking"]);

        await command.Execute();

        var request = Assert.Single(requests);
        Assert.True(request.AllowBlanking);
        Assert.Null(request.ConfirmedBlankings);
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Warning && e.Message.Contains("--allow-blanking") && e.Message.Contains("EVERY"));
    }

    [Fact]
    public async Task UpdateBlueprint_AllowBlankingWinsOverConfirmBlanking()
    {
        var (client, requests) = NewApplyClient(new BlueprintUpdateResultDto { Success = true });
        var logger = new RecordingLogger<UpdateBlueprint>();
        var command = NewUpdate(client, logger);
        command.CommandArgumentValue.ParseLayer(["-tv", "v-1", "-ab", "-cb", $"{RtId1}:configuration"]);

        await command.Execute();

        var request = Assert.Single(requests);
        Assert.True(request.AllowBlanking);
        Assert.Null(request.ConfirmedBlankings);
        Assert.Contains(logger.Entries, e => e.Message.Contains("--confirm-blanking is ignored"));
    }

    [Fact]
    public async Task UpdateBlueprint_MalformedConfirmation_FailsBeforeCallingTheService()
    {
        var (client, requests) = NewApplyClient(new BlueprintUpdateResultDto { Success = true });
        var command = NewUpdate(client, new RecordingLogger<UpdateBlueprint>());
        command.CommandArgumentValue.ParseLayer(["-tv", "v-1", "-cb", "configuration"]);

        await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Empty(requests);
    }

    // ---------- UpdateBlueprint output ----------

    [Fact]
    public async Task UpdateBlueprint_WithoutConfirmation_ListsTheKeptValuesAndSaysHowToConfirm()
    {
        var (client, _) = NewApplyClient(new BlueprintUpdateResultDto
        {
            Success = true,
            BlankedAttributes = [Blanked(RtId1, "configuration", applied: false)]
        });
        var logger = new RecordingLogger<UpdateBlueprint>();
        var command = NewUpdate(client, logger);
        command.CommandArgumentValue.ParseLayer(["-tv", "Eda-1.2.0"]);

        await command.Execute();

        var output = Warnings(logger);
        Assert.Contains("1 tenant value(s) were KEPT", output);
        Assert.Contains(RtId1, output);
        Assert.Contains("configuration", output);
        Assert.Contains("string (223 chars)", output);
        Assert.Contains("empty string", output);
        Assert.Contains($"--confirm-blanking {RtId1}:configuration", output);
        Assert.DoesNotContain("BLANKED on your confirmation", output);
    }

    [Fact]
    public async Task UpdateBlueprint_WithConfirmation_SaysWhichValuesWereBlankedAndWhichKept()
    {
        var (client, _) = NewApplyClient(new BlueprintUpdateResultDto
        {
            Success = true,
            BlankedAttributes =
            [
                Blanked(RtId1, "configuration", applied: true),
                Blanked(RtId2, "password", applied: false)
            ]
        });
        var logger = new RecordingLogger<UpdateBlueprint>();
        var command = NewUpdate(client, logger);
        command.CommandArgumentValue.ParseLayer(["-tv", "Eda-1.2.0", "-cb", $"{RtId1}:configuration"]);

        await command.Execute();

        var output = Warnings(logger);
        var blankedAt = output.IndexOf("1 tenant value(s) were BLANKED on your confirmation", StringComparison.Ordinal);
        var keptAt = output.IndexOf("1 tenant value(s) were KEPT", StringComparison.Ordinal);
        Assert.True(blankedAt >= 0 && keptAt > blankedAt, output);
        Assert.Contains("configuration", output[blankedAt..keptAt]);
        Assert.DoesNotContain("password", output[blankedAt..keptAt]);
        Assert.Contains("password", output[keptAt..]);
    }

    [Fact]
    public async Task UpdateBlueprint_ConfirmationThatIsNoCandidate_IsReportedAsIgnored()
    {
        var (client, _) = NewApplyClient(new BlueprintUpdateResultDto
        {
            Success = true,
            BlankedAttributes = [Blanked(RtId1, "configuration", applied: false)]
        });
        var logger = new RecordingLogger<UpdateBlueprint>();
        var command = NewUpdate(client, logger);
        command.CommandArgumentValue.ParseLayer(["-tv", "v-1", "-cb", $"{RtId2}:typo"]);

        await command.Execute();

        Assert.Contains($"'{RtId2}:typo' is not a blanking candidate", Warnings(logger));
    }

    [Fact]
    public async Task UpdateBlueprint_AgainstOldService_NoReport_StillSucceedsQuietly()
    {
        var (client, _) = NewApplyClient(new BlueprintUpdateResultDto { Success = true });
        var logger = new RecordingLogger<UpdateBlueprint>();
        var command = NewUpdate(client, logger);
        command.CommandArgumentValue.ParseLayer(["-tv", "v-1"]);

        await command.Execute();

        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
        Assert.Contains(logger.Entries, e => e.Message.Contains("Blueprint update applied"));
    }

    // ---------- PreviewBlueprintUpdate ----------

    [Fact]
    public async Task Preview_PrintsTheBlankingTable_WithoutAnyValue()
    {
        var (client, console, lines) = NewPreview(
            Blanked(RtId1, "configuration", applied: false), Blanked(RtId2, "password", applied: false));
        var logger = new RecordingLogger<PreviewBlueprintUpdate>();
        var command = NewPreviewCommand(client, console, logger);
        command.CommandArgumentValue.ParseLayer(["-tv", "Eda-1.2.0"]);

        await command.Execute();

        var table = Warnings(logger);
        Assert.Contains("would blank 2 tenant value(s)", table);
        Assert.Contains("Entity (rtId)", table);
        Assert.Contains("Attribute", table);
        Assert.Contains("Current", table);
        Assert.Contains("Incoming", table);
        foreach (var expected in new[] { RtId1, RtId2, "configuration", "password", "SeedEmpty", "string (223 chars)", "empty string" })
        {
            Assert.Contains(expected, table);
        }

        // The JSON result keeps going to standard output, now with the blanked list.
        Assert.Contains("\"BlankedAttributes\"", Assert.Single(lines));
    }

    [Fact]
    public void BlankedAttributeDto_CarriesDescriptionsOnly_NeverAValue()
    {
        // The table is built from these members. A value-carrying member (a credential may be blanked) must
        // not appear without this test - and the table code - being looked at again.
        var members = typeof(BlueprintBlankedAttributeDto).GetProperties().Select(p => p.Name).Order().ToArray();

        Assert.Equal(
            ["AppliedOnUpdate", "AttributeName", "CkTypeId", "CurrentSummary", "IncomingSummary", "Reason", "RtId"],
            members);
    }

    [Fact]
    public void Preview_TableColumnsAreAligned()
    {
        var table = BlueprintBlanking.FormatTable(
            [Blanked(RtId1, "configuration", false), Blanked(RtId2, "pw", false)]);

        Assert.Equal(3, table.Count);
        var attributeColumn = table[0].IndexOf("Attribute", StringComparison.Ordinal);
        Assert.Equal(attributeColumn, table[1].IndexOf("configuration", StringComparison.Ordinal));
        Assert.Equal(attributeColumn, table[2].IndexOf("pw", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Preview_WithoutCandidates_PrintsNoTable_AndFailOnBlankingPasses()
    {
        var (client, console, _) = NewPreview();
        var logger = new RecordingLogger<PreviewBlueprintUpdate>();
        var command = NewPreviewCommand(client, console, logger);
        command.CommandArgumentValue.ParseLayer(["-tv", "Eda-1.2.0", "--failOnBlanking"]);

        await command.Execute();

        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Preview_FailOnBlanking_WithCandidates_FailsAfterPrintingTheTable()
    {
        var (client, console, lines) = NewPreview(Blanked(RtId1, "configuration", applied: false));
        var logger = new RecordingLogger<PreviewBlueprintUpdate>();
        var command = NewPreviewCommand(client, console, logger);
        command.CommandArgumentValue.ParseLayer(["-tv", "Eda-1.2.0", "-fb"]);

        var exception = await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Contains("1 tenant value(s)", exception.Message);
        Assert.Contains("--failOnBlanking", exception.Message);
        Assert.Single(lines);
        Assert.Contains("configuration", Warnings(logger));
    }

    [Fact]
    public async Task Preview_WithCandidatesButWithoutFailOnBlanking_Succeeds()
    {
        var (client, console, _) = NewPreview(Blanked(RtId1, "configuration", applied: false));
        var command = NewPreviewCommand(client, console, new RecordingLogger<PreviewBlueprintUpdate>());
        command.CommandArgumentValue.ParseLayer(["-tv", "Eda-1.2.0"]);

        await command.Execute();
    }

    // ---------- tenant-owned seed entities (AB#6454) ----------

    private static BlueprintTenantOwnedEntityDto Kept(string key, string rtId) =>
        new() { Key = key, CkTypeId = "Test/Rule", EntityId = rtId, WellKnownName = key };

    private static BlueprintTenantOwnedEntityDto Gone(string key) =>
        new() { Key = key, CkTypeId = "Test/Rule" };

    private static string Infos<T>(RecordingLogger<T> logger) =>
        string.Join('\n', logger.Entries.Where(e => e.Level == LogLevel.Information).Select(e => e.Message));

    [Fact]
    public async Task Preview_PrintsTheTenantOwnedSections_WithKeyTypeAndRtIdOrDash()
    {
        var client = A.Fake<IAssetServicesClient>();
        A.CallTo(() => client.PreviewBlueprintUpdateAsync(A<string>._, A<BlueprintUpdateRequestDto>._))
            .Returns(new BlueprintUpdatePreviewDto
            {
                TargetVersion = "Eda-1.2.0",
                TenantOwnedSkipped = [Kept("OwnedKept", RtId1)],
                TenantOwnedStaysDeleted = [Gone("OwnedGone")]
            });
        var logger = new RecordingLogger<PreviewBlueprintUpdate>();
        var command = NewPreviewCommand(client, A.Fake<IConsoleService>(), logger);
        command.CommandArgumentValue.ParseLayer(["-tv", "Eda-1.2.0", "--failOnBlanking"]);

        await command.Execute(); // --failOnBlanking is not affected: these lists are not blanking

        var output = Infos(logger);
        var skippedAt = output.IndexOf("tenant-owned, skipped", StringComparison.Ordinal);
        var deletedAt = output.IndexOf("deleted by the tenant would stay deleted", StringComparison.Ordinal);
        Assert.True(skippedAt >= 0 && deletedAt > skippedAt, output);
        Assert.Contains("OwnedKept", output[skippedAt..deletedAt]);
        Assert.Contains("Test/Rule", output[skippedAt..deletedAt]);
        Assert.Contains(RtId1, output[skippedAt..deletedAt]);
        Assert.Contains("OwnedGone", output[deletedAt..]);
        Assert.DoesNotContain(RtId1, output[deletedAt..]);
        Assert.Matches(@"OwnedGone\s+Test/Rule\s+-", output[deletedAt..]);
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Preview_WithEmptyTenantOwnedLists_PrintsNothingExtra()
    {
        var (client, console, _) = NewPreview();
        var logger = new RecordingLogger<PreviewBlueprintUpdate>();
        var command = NewPreviewCommand(client, console, logger);
        command.CommandArgumentValue.ParseLayer(["-tv", "Eda-1.2.0"]);

        await command.Execute();

        Assert.DoesNotContain("tenant-owned", Infos(logger));
        Assert.DoesNotContain("stay deleted", Infos(logger));
        Assert.DoesNotContain("stays deleted", Infos(logger));
    }

    [Fact]
    public async Task UpdateBlueprint_PrintsTheTenantOwnedSections_AndNothingForEmptyLists()
    {
        var (client, _) = NewApplyClient(new BlueprintUpdateResultDto
        {
            Success = true,
            TenantOwnedSkipped = [Kept("OwnedKept", RtId1)],
            TenantOwnedStaysDeleted = [Gone("OwnedGone")]
        });
        var logger = new RecordingLogger<UpdateBlueprint>();
        var command = NewUpdate(client, logger);
        command.CommandArgumentValue.ParseLayer(["-tv", "Eda-1.2.0"]);

        await command.Execute();

        var output = Infos(logger);
        Assert.Contains("were left untouched (tenant-owned, skipped)", output);
        Assert.Contains("deleted by the tenant stay deleted", output);
        Assert.Contains("OwnedKept", output);
        Assert.Contains(RtId1, output);
        Assert.Matches(@"OwnedGone\s+Test/Rule\s+-", output);

        var (emptyClient, _) = NewApplyClient(new BlueprintUpdateResultDto { Success = true });
        var emptyLogger = new RecordingLogger<UpdateBlueprint>();
        var emptyCommand = NewUpdate(emptyClient, emptyLogger);
        emptyCommand.CommandArgumentValue.ParseLayer(["-tv", "Eda-1.2.0"]);
        await emptyCommand.Execute();
        Assert.DoesNotContain("tenant-owned", Infos(emptyLogger));
    }

    [Fact]
    public void TenantOwnedEntityDto_CarriesIdentityOnly_NeverAValue()
    {
        var members = typeof(BlueprintTenantOwnedEntityDto).GetProperties().Select(p => p.Name).Order().ToArray();

        Assert.Equal(["CkTypeId", "EntityId", "Key", "WellKnownName"], members);
    }

    [Fact]
    public void FormatTable_PrintsTheResetToDefaultReasonAsReported()
    {
        var row = Blanked(RtId1, "threshold", false);
        row.Reason = "ResetToDefault";

        var table = BlueprintBlanking.FormatTable([row]);

        Assert.Contains("ResetToDefault", table[1]);
    }

    // ---------- helpers ----------

    private static BlueprintBlankedAttributeDto Blanked(string rtId, string attribute, bool applied) => new()
    {
        RtId = rtId,
        CkTypeId = "System.Communication/Adapter",
        AttributeName = attribute,
        Reason = "SeedEmpty",
        CurrentSummary = "string (223 chars)",
        IncomingSummary = "empty string",
        AppliedOnUpdate = applied
    };

    private static string Warnings<T>(RecordingLogger<T> logger) =>
        string.Join('\n', logger.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message));

    private static (IAssetServicesClient Client, List<BlueprintUpdateRequestDto> Requests) NewApplyClient(
        BlueprintUpdateResultDto result)
    {
        var requests = new List<BlueprintUpdateRequestDto>();
        var client = A.Fake<IAssetServicesClient>();
        A.CallTo(() => client.ApplyBlueprintUpdateAsync(A<string>._, A<BlueprintUpdateRequestDto>._))
            .Invokes((string _, BlueprintUpdateRequestDto request) => requests.Add(request))
            .Returns(result);
        return (client, requests);
    }

    private static UpdateBlueprint NewUpdate(IAssetServicesClient client, ILogger<UpdateBlueprint> logger) =>
        new(logger, Options.Create(new OctoToolOptions { TenantId = "acme" }), client,
            A.Fake<IAuthenticationService>());

    private static (IAssetServicesClient Client, IConsoleService Console, List<string> Lines) NewPreview(
        params BlueprintBlankedAttributeDto[] blanked)
    {
        var client = A.Fake<IAssetServicesClient>();
        A.CallTo(() => client.PreviewBlueprintUpdateAsync(A<string>._, A<BlueprintUpdateRequestDto>._))
            .Returns(new BlueprintUpdatePreviewDto { TargetVersion = "Eda-1.2.0", BlankedAttributes = [.. blanked] });
        var lines = new List<string>();
        var console = A.Fake<IConsoleService>();
        A.CallTo(() => console.WriteLine(A<string>._)).Invokes((string line) => lines.Add(line));
        return (client, console, lines);
    }

    private static PreviewBlueprintUpdate NewPreviewCommand(
        IAssetServicesClient client, IConsoleService console, ILogger<PreviewBlueprintUpdate> logger) =>
        new(logger, console, Options.Create(new OctoToolOptions { TenantId = "acme" }), client,
            A.Fake<IAuthenticationService>());
}
