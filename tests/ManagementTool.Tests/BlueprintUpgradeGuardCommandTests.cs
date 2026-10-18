using System.Net;
using FakeItEasy;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Blueprints;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Blueprints;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.CkModelCatalog;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#6871 (tenant upgrade guard for blueprints, staging-1 tenant sbeg: Registration 2.8.0 exact-pins Basic-2.4.0):
///     <c>PreviewBlueprintUpdate</c> and <c>UpdateBlueprint --dry-run</c> list the blockers and write nothing,
///     <c>UpdateBlueprint</c> / <c>InstallBlueprint</c> end with an error and the reason when the service refuses with
///     CK_UPGRADE_BLOCKED, the override reaches the service only when asked for, and old services keep working.
/// </summary>
public sealed class BlueprintUpgradeGuardCommandTests
{
    private const string Hint = "update EnergyCommunity.Registration to 2.10.0 first";

    private static CkUpgradeBlockerDto Blocker() => new()
    {
        Dependent = "EnergyCommunity.Registration-2.8.0", Update = "Basic-2.5.0", Kind = "ExactPin",
        Subject = "Basic-2.4.0", Code = "CK_UPGRADE_EXACT_PIN", Message = "exact pin Basic-2.4.0: Basic-2.5.0 replaces it",
        ResolveHint = Hint
    };

    private static CkUpgradeBlockedException Refusal() =>
        new("Blueprint Shop-2.0.0 would install Basic-2.5.0, which would break 1 installed model(s).",
            ["Basic-2.5.0"], [Blocker()]);

    private static string Joined<T>(RecordingLogger<T> logger, LogLevel? level = null) =>
        string.Join("\n", logger.Entries.Where(e => level == null || e.Level == level).Select(e => e.Message));

    // ---- PreviewBlueprintUpdate ----

    private static (PreviewBlueprintUpdate Command, RecordingLogger<PreviewBlueprintUpdate> Logger) NewPreview(
        BlueprintUpdatePreviewDto preview)
    {
        var client = A.Fake<IAssetServicesClient>();
        A.CallTo(() => client.PreviewBlueprintUpdateAsync(A<string>._, A<BlueprintUpdateRequestDto>._)).Returns(preview);
        var logger = new RecordingLogger<PreviewBlueprintUpdate>();
        return (new PreviewBlueprintUpdate(logger, A.Fake<IConsoleService>(),
            Options.Create(new OctoToolOptions { TenantId = "sbeg" }), client, A.Fake<IAuthenticationService>()), logger);
    }

    private static BlueprintUpdatePreviewDto BlockedPreview() => new()
    {
        TargetVersion = "Shop-2.0.0", IsBlocked = true, CkUpdateSet = ["Basic-2.5.0"], Blockers = [Blocker()]
    };

    [Fact]
    public async Task Preview_Blocked_ListsTheBlockerWithItsHint_AndSucceeds()
    {
        var (command, logger) = NewPreview(BlockedPreview());
        command.CommandArgumentValue.ParseLayer(["-tv", "Shop-2.0.0"]);

        await command.Execute();

        var warnings = Joined(logger, LogLevel.Warning);
        Assert.Contains("EnergyCommunity.Registration-2.8.0", warnings);
        Assert.Contains("Basic-2.4.0", warnings);
        Assert.Contains("Basic-2.5.0", warnings);
        Assert.Contains(Hint, warnings);
        Assert.Contains("--force", warnings);
        Assert.Contains("CK_UPGRADE_BLOCKED", warnings);
    }

    [Fact]
    public async Task Preview_Blocked_WithFailOnBlocked_ExitsWithAnError()
    {
        var (command, _) = NewPreview(BlockedPreview());
        command.CommandArgumentValue.ParseLayer(["-tv", "Shop-2.0.0", "--failOnBlocked"]);

        var ex = await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Contains("--failOnBlocked", ex.Message);
        Assert.Contains("1 installed model", ex.Message);
    }

    [Fact]
    public async Task Preview_NotBlocked_PrintsNothingAboutTheGuard_EvenWithFailOnBlocked()
    {
        var (command, logger) = NewPreview(new BlueprintUpdatePreviewDto { TargetVersion = "Shop-2.0.0" });
        command.CommandArgumentValue.ParseLayer(["-tv", "Shop-2.0.0", "--failOnBlocked"]);

        await command.Execute();

        Assert.DoesNotContain("CK_UPGRADE_BLOCKED", Joined(logger));
    }

    // ---- UpdateBlueprint ----

    private static (IAssetServicesClient Client, List<BlueprintUpdateRequestDto> Requests) NewApplyClient(
        BlueprintUpdateResultDto? result = null, Exception? throws = null)
    {
        var requests = new List<BlueprintUpdateRequestDto>();
        var client = A.Fake<IAssetServicesClient>();
        var call = A.CallTo(() => client.ApplyBlueprintUpdateAsync(A<string>._, A<BlueprintUpdateRequestDto>._));
        call.Invokes((string _, BlueprintUpdateRequestDto request) => requests.Add(request));
        if (throws != null)
        {
            call.Throws(throws);
        }
        else
        {
            call.Returns(result ?? new BlueprintUpdateResultDto { Success = true });
        }

        return (client, requests);
    }

    private static UpdateBlueprint NewUpdate(IAssetServicesClient client, RecordingLogger<UpdateBlueprint> logger) =>
        new(logger, Options.Create(new OctoToolOptions { TenantId = "sbeg" }), client, A.Fake<IAuthenticationService>());

    [Fact]
    public async Task Update_Refused_EndsWithAnErrorAndTheReason_NothingIsClaimedAsApplied()
    {
        var (client, requests) = NewApplyClient(throws: Refusal());
        var logger = new RecordingLogger<UpdateBlueprint>();
        var command = NewUpdate(client, logger);
        command.CommandArgumentValue.ParseLayer(["-tv", "Shop-2.0.0"]);

        var ex = await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Contains("CK_UPGRADE_BLOCKED", ex.Message);
        Assert.Contains("--force", ex.Message);
        Assert.False(Assert.Single(requests).ForceBreakingUpdate);
        var errors = Joined(logger, LogLevel.Error);
        Assert.Contains("nothing was changed", errors);
        Assert.Contains("EnergyCommunity.Registration-2.8.0", errors);
        Assert.Contains(Hint, errors);
        Assert.DoesNotContain("Blueprint update applied", Joined(logger));
    }

    [Theory]
    [InlineData("--force")]
    [InlineData("-f")]
    [InlineData("--forceBreakingUpdate")]
    public async Task Update_Force_SendsTheOverride_AndWarnsWithTheForcedBlockers(string option)
    {
        var (client, requests) = NewApplyClient(new BlueprintUpdateResultDto
        {
            Success = true, CkUpdateSet = ["Basic-2.5.0"], Blockers = [Blocker()]
        });
        var logger = new RecordingLogger<UpdateBlueprint>();
        var command = NewUpdate(client, logger);
        command.CommandArgumentValue.ParseLayer(["-tv", "Shop-2.0.0", option]);

        await command.Execute();

        Assert.True(Assert.Single(requests).ForceBreakingUpdate);
        var warnings = Joined(logger, LogLevel.Warning);
        Assert.Contains("ResolveFailed", warnings);
        Assert.Contains("EnergyCommunity.Registration-2.8.0", warnings);
    }

    [Fact]
    public async Task Update_WithoutForce_SendsNoOverride_AndSaysNothingAboutTheGuard()
    {
        var (client, requests) = NewApplyClient();
        var logger = new RecordingLogger<UpdateBlueprint>();
        var command = NewUpdate(client, logger);
        command.CommandArgumentValue.ParseLayer(["-tv", "Shop-2.0.0"]);

        await command.Execute();

        Assert.False(Assert.Single(requests).ForceBreakingUpdate);
        Assert.DoesNotContain("CK_UPGRADE_BLOCKED", Joined(logger));
        Assert.DoesNotContain("ResolveFailed", Joined(logger));
    }

    [Fact]
    public async Task Update_DryRun_ListsTheBlockers_AndStatesNothingWasChanged()
    {
        var (client, requests) = NewApplyClient(new BlueprintUpdateResultDto
        {
            Success = true, IsBlocked = true, CkUpdateSet = ["Basic-2.5.0"], Blockers = [Blocker()]
        });
        var logger = new RecordingLogger<UpdateBlueprint>();
        var command = NewUpdate(client, logger);
        command.CommandArgumentValue.ParseLayer(["-tv", "Shop-2.0.0", "--dry-run"]);

        await command.Execute();

        Assert.True(Assert.Single(requests).DryRun);
        var all = Joined(logger);
        Assert.Contains("simulated, nothing was changed", all);
        var warnings = Joined(logger, LogLevel.Warning);
        Assert.Contains("EnergyCommunity.Registration-2.8.0", warnings);
        Assert.Contains("Basic-2.4.0", warnings);
        Assert.Contains(Hint, warnings);
        Assert.Contains("refused", warnings);
    }

    [Fact]
    public async Task Update_AnOldService_WithoutTheGuardMembers_BehavesAsBefore()
    {
        var (client, _) = NewApplyClient(new BlueprintUpdateResultDto { Success = true });
        var logger = new RecordingLogger<UpdateBlueprint>();
        var command = NewUpdate(client, logger);
        command.CommandArgumentValue.ParseLayer(["-tv", "Shop-2.0.0", "--dry-run"]);

        await command.Execute();

        Assert.DoesNotContain("CK_UPGRADE_BLOCKED", Joined(logger));
    }

    // ---- InstallBlueprint ----

    private static (InstallBlueprint Command, RecordingLogger<InstallBlueprint> Logger, List<BlueprintApplyRequestDto> Requests)
        NewInstall(BlueprintApplyResultDto? result = null, Exception? throws = null)
    {
        var requests = new List<BlueprintApplyRequestDto>();
        var client = A.Fake<IAssetServicesClient>();
        var call = A.CallTo(() => client.ApplyBlueprintAsync(A<string>._, A<BlueprintApplyRequestDto>._));
        call.Invokes((string _, BlueprintApplyRequestDto request) => requests.Add(request));
        if (throws != null)
        {
            call.Throws(throws);
        }
        else
        {
            call.Returns(result ?? new BlueprintApplyResultDto { Success = true, BlueprintId = "Shop-2.0.0" });
        }

        var logger = new RecordingLogger<InstallBlueprint>();
        return (new InstallBlueprint(logger, A.Fake<IConsoleService>(),
            Options.Create(new OctoToolOptions { TenantId = "sbeg" }), client, A.Fake<IAuthenticationService>()),
            logger, requests);
    }

    [Fact]
    public async Task Install_Refused_EndsWithAnErrorAndTheReason()
    {
        var (command, logger, requests) = NewInstall(throws: Refusal());
        command.CommandArgumentValue.ParseLayer(["-b", "Shop-2.0.0"]);

        var ex = await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Contains("CK_UPGRADE_BLOCKED", ex.Message);
        Assert.Contains("--forceBreakingUpdate", ex.Message);
        var request = Assert.Single(requests);
        Assert.False(request.ForceBreakingUpdate);
        Assert.False(request.Force);
        Assert.Contains(Hint, Joined(logger, LogLevel.Error));
    }

    [Fact]
    public async Task Install_ForceBreakingUpdate_SendsTheOverride_AndKeepsForceSeparate()
    {
        var (command, logger, requests) = NewInstall(new BlueprintApplyResultDto
        {
            Success = true, BlueprintId = "Shop-2.0.0", Blockers = [Blocker()]
        });
        command.CommandArgumentValue.ParseLayer(["-b", "Shop-2.0.0", "--forceBreakingUpdate"]);

        await command.Execute();

        var request = Assert.Single(requests);
        Assert.True(request.ForceBreakingUpdate);
        Assert.False(request.Force, "--force means re-apply the seed data, not the guard override");
        Assert.Contains("ResolveFailed", Joined(logger, LogLevel.Warning));
    }

    [Fact]
    public async Task Install_Force_StillMeansReApply_AndDoesNotOverrideTheGuard()
    {
        var (command, _, requests) = NewInstall();
        command.CommandArgumentValue.ParseLayer(["-b", "Shop-2.0.0", "--force"]);

        await command.Execute();

        var request = Assert.Single(requests);
        Assert.True(request.Force);
        Assert.False(request.ForceBreakingUpdate);
    }

    [Fact]
    public async Task Install_NothingBreaks_IsUnchanged()
    {
        var (command, logger, _) = NewInstall();
        command.CommandArgumentValue.ParseLayer(["-b", "Shop-2.0.0"]);

        await command.Execute();

        Assert.DoesNotContain("ResolveFailed", Joined(logger));
        Assert.Contains(logger.Entries, e => e.Message.Contains("applied to tenant"));
    }

    [Fact]
    public void The409OfTheService_IsTheStructuredException()
    {
        // The command relies on the SDK translating CK_UPGRADE_BLOCKED; a plain 409 stays a generic failure.
        Assert.Equal(HttpStatusCode.Conflict, Refusal().HttpStatusCode);
    }
}
