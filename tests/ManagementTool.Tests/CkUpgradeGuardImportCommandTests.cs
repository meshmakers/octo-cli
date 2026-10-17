using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.CkModelLibraries;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.CkModelCatalog;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#6870 (tenant upgrade guard enforcement, staging-1 tenant sbeg: Registration 2.8.0 exact-pins Basic-2.4.0 /
///     Basic.Energy-1.9.0): <c>ImportFromCatalog</c> stops on a blocked import unless <c>--force</c>, and sends the
///     force flag only then; <c>FixAll</c> skips blocked models, imports the rest and never sends the force flag.
/// </summary>
public sealed class CkUpgradeGuardImportCommandTests
{
    private const string JobId = "job-6870";
    private const string Hint = "update EnergyCommunity.Registration to 2.10.0 first";

    private static CkUpgradeBlockerDto Blocker(string update, string pin) => new()
    {
        Dependent = "EnergyCommunity.Registration-2.8.0", Update = update, Kind = "ExactPin", Subject = pin,
        Code = "CK_UPGRADE_EXACT_PIN", Message = $"exact pin {pin}: {update} replaces it", ResolveHint = Hint
    };

    private static DependencyResolutionResponseDto Tree(string rootId, params string[] dependencyIds) => new()
    {
        RootModel = new DependencyResolutionItemDto
        {
            ModelId = rootId, Action = "update",
            Dependencies = dependencyIds.Select(d => new DependencyResolutionItemDto { ModelId = d, Action = "update" }).ToList()
        }
    };

    private static IBotServicesClient BotClient()
    {
        var bot = A.Fake<IBotServicesClient>();
        A.CallTo(() => bot.GetImportJobStatus(JobId)).Returns(new JobDto { Id = JobId, Status = "Succeeded" });
        return bot;
    }

    private static IAssetServicesClient AssetClient()
    {
        var asset = A.Fake<IAssetServicesClient>();
        A.CallTo(() => asset.ImportFromCatalogBatchAsync(A<string>._, A<ImportFromCatalogBatchRequestDto>._))
            .Returns(new BatchImportResponseDto { JobId = JobId });
        return asset;
    }

    private static ImportFromCatalogCommand NewImport(IAssetServicesClient asset, RecordingLogger<ImportFromCatalogCommand> logger) =>
        new(logger, Options.Create(new OctoToolOptions { TenantId = "sbeg" }), asset, BotClient(),
            A.Fake<IAuthenticationService>());

    private static FixAllCommand NewFixAll(IAssetServicesClient asset, RecordingLogger<FixAllCommand> logger) =>
        new(logger, Options.Create(new OctoToolOptions { TenantId = "sbeg" }), asset, BotClient(),
            A.Fake<IAuthenticationService>(), A.Fake<IConfirmationService>());

    private static void Resolves(IAssetServicesClient asset, BatchDependencyResolutionResponseDto response) =>
        A.CallTo(() => asset.ResolveDependenciesBatchAsync(A<string>._, A<List<ImportFromCatalogRequestDto>>._))
            .Returns(response);

    // ---- ImportFromCatalog ----

    [Fact]
    public async Task ImportFromCatalog_Blocked_WithoutForce_StopsBeforeImporting_AndPrintsTheReasonAndHint()
    {
        var asset = AssetClient();
        Resolves(asset, new BatchDependencyResolutionResponseDto
        {
            ModelsToImport = ["Basic-2.5.0"], IsBlocked = true, Blockers = [Blocker("Basic-2.5.0", "Basic-2.4.0")]
        });
        var logger = new RecordingLogger<ImportFromCatalogCommand>();
        var command = NewImport(asset, logger);
        command.CommandArgumentValue.ParseLayer(["-cn", "PublicGitHubCatalog", "-m", "Basic-2.5.0"]);

        var ex = await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Contains("--force", ex.Message);
        Assert.Contains("CK_UPGRADE_BLOCKED", ex.Message);
        A.CallTo(() => asset.ImportFromCatalogBatchAsync(A<string>._, A<ImportFromCatalogBatchRequestDto>._))
            .MustNotHaveHappened();
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("EnergyCommunity.Registration-2.8.0"));
        Assert.Contains(logger.Entries, e => e.Message.Contains(Hint));
    }

    [Fact]
    public async Task ImportFromCatalog_Blocked_WithForce_SendsTheForceFlag_AndWarns()
    {
        var asset = AssetClient();
        Resolves(asset, new BatchDependencyResolutionResponseDto
        {
            ModelsToImport = ["Basic-2.5.0"], IsBlocked = true, Blockers = [Blocker("Basic-2.5.0", "Basic-2.4.0")]
        });
        var logger = new RecordingLogger<ImportFromCatalogCommand>();
        var command = NewImport(asset, logger);
        command.CommandArgumentValue.ParseLayer(["-cn", "PublicGitHubCatalog", "-m", "Basic-2.5.0", "--force"]);

        await command.Execute();

        A.CallTo(() => asset.ImportFromCatalogBatchAsync("sbeg",
                A<ImportFromCatalogBatchRequestDto>.That.Matches(r => r.ForceBreakingUpdate && r.ModelIds.Count == 1)))
            .MustHaveHappenedOnceExactly();
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("--force"));
    }

    [Fact]
    public async Task ImportFromCatalog_NotBlocked_DoesNotSendTheForceFlag()
    {
        var asset = AssetClient();
        Resolves(asset, new BatchDependencyResolutionResponseDto { ModelsToImport = ["Basic-2.5.0"] });
        var command = NewImport(asset, new RecordingLogger<ImportFromCatalogCommand>());
        command.CommandArgumentValue.ParseLayer(["-cn", "PublicGitHubCatalog", "-m", "Basic-2.5.0"]);

        await command.Execute();

        A.CallTo(() => asset.ImportFromCatalogBatchAsync("sbeg",
                A<ImportFromCatalogBatchRequestDto>.That.Matches(r => !r.ForceBreakingUpdate)))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ImportFromCatalog_ServiceRefuses409_PrintsTheBlockers_AndNamesForce()
    {
        // An older service does not preview the blockers in ResolveDependenciesBatch but still refuses the import.
        var asset = AssetClient();
        Resolves(asset, new BatchDependencyResolutionResponseDto { ModelsToImport = ["Basic-2.5.0"] });
        A.CallTo(() => asset.ImportFromCatalogBatchAsync(A<string>._, A<ImportFromCatalogBatchRequestDto>._))
            .Throws(new CkUpgradeBlockedException("d", ["Basic-2.5.0"], [Blocker("Basic-2.5.0", "Basic-2.4.0")]));
        var logger = new RecordingLogger<ImportFromCatalogCommand>();
        var command = NewImport(asset, logger);
        command.CommandArgumentValue.ParseLayer(["-cn", "PublicGitHubCatalog", "-m", "Basic-2.5.0"]);

        var ex = await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Contains("--force", ex.Message);
        Assert.Contains(logger.Entries, e => e.Message.Contains(Hint));
    }

    // ---- FixAll ----

    private static CkModelLibraryStatusItemDto Row(string name, string installed, string catalog, bool blocked) => new()
    {
        Name = name, InstalledVersion = installed, CatalogVersion = catalog, HasUpdate = true, NeedsAction = !blocked,
        IsBlocked = blocked, CatalogName = "PublicGitHubCatalog", FullModelId = $"{name}-{catalog}",
        Blockers = blocked ? [Blocker($"{name}-{catalog}", $"{name}-{installed}")] : []
    };

    [Fact]
    public async Task FixAll_ImportsTheUnblockedModels_SkipsTheBlockedOnesWithReasonAndHint_AndNeverForces()
    {
        var asset = AssetClient();
        A.CallTo(() => asset.GetLibraryStatusAsync(A<string>._)).Returns(new CkModelLibraryStatusResponseDto
        {
            Items = [Row("Basic", "2.4.0", "2.5.0", blocked: true), Row("Other", "1.0.0", "1.1.0", blocked: false)]
        });
        Resolves(asset, new BatchDependencyResolutionResponseDto
        {
            ModelsToImport = ["Other-1.1.0"], DependencyTrees = [Tree("Other-1.1.0")]
        });
        var logger = new RecordingLogger<FixAllCommand>();
        var command = NewFixAll(asset, logger);
        command.CommandArgumentValue.ParseLayer(["-y"]);

        await command.Execute();

        A.CallTo(() => asset.ResolveDependenciesBatchAsync("sbeg",
                A<List<ImportFromCatalogRequestDto>>.That.Matches(r => r.Count == 1 && r[0].ModelId == "Other-1.1.0")))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => asset.ImportFromCatalogBatchAsync("sbeg",
                A<ImportFromCatalogBatchRequestDto>.That.Matches(r =>
                    !r.ForceBreakingUpdate && r.ModelIds.SequenceEqual(new[] { "Other-1.1.0" }))))
            .MustHaveHappenedOnceExactly();
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("skipped"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("Basic"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("EnergyCommunity.Registration-2.8.0"));
        Assert.Contains(logger.Entries, e => e.Message.Contains(Hint));
    }

    [Fact]
    public async Task FixAll_OnlyBlockedModels_ImportsNothing_AndDoesNotResolve()
    {
        var asset = AssetClient();
        A.CallTo(() => asset.GetLibraryStatusAsync(A<string>._)).Returns(new CkModelLibraryStatusResponseDto
        {
            Items = [Row("Basic", "2.4.0", "2.5.0", blocked: true)]
        });
        var command = NewFixAll(asset, new RecordingLogger<FixAllCommand>());
        command.CommandArgumentValue.ParseLayer(["-y"]);

        await command.Execute();

        A.CallTo(() => asset.ResolveDependenciesBatchAsync(A<string>._, A<List<ImportFromCatalogRequestDto>>._))
            .MustNotHaveHappened();
        A.CallTo(() => asset.ImportFromCatalogBatchAsync(A<string>._, A<ImportFromCatalogBatchRequestDto>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task FixAll_ResolvedSetIsBlockedByADependency_DropsTheModelsThatPullItIn_AndImportsTheRest()
    {
        // "Energy" pulls in Basic-2.5.0 as a dependency, which breaks Registration 2.8.0; "Other" is unrelated.
        var asset = AssetClient();
        A.CallTo(() => asset.GetLibraryStatusAsync(A<string>._)).Returns(new CkModelLibraryStatusResponseDto
        {
            Items = [Row("Energy", "1.0.0", "1.1.0", blocked: false), Row("Other", "1.0.0", "1.1.0", blocked: false)]
        });
        A.CallTo(() => asset.ResolveDependenciesBatchAsync(A<string>._,
                A<List<ImportFromCatalogRequestDto>>.That.Matches(r => r.Count == 2)))
            .Returns(new BatchDependencyResolutionResponseDto
            {
                ModelsToImport = ["Basic-2.5.0", "Energy-1.1.0", "Other-1.1.0"],
                DependencyTrees = [Tree("Energy-1.1.0", "Basic-2.5.0"), Tree("Other-1.1.0")],
                IsBlocked = true, Blockers = [Blocker("Basic-2.5.0", "Basic-2.4.0")]
            });
        A.CallTo(() => asset.ResolveDependenciesBatchAsync(A<string>._,
                A<List<ImportFromCatalogRequestDto>>.That.Matches(r => r.Count == 1)))
            .Returns(new BatchDependencyResolutionResponseDto
            {
                ModelsToImport = ["Other-1.1.0"], DependencyTrees = [Tree("Other-1.1.0")]
            });
        var logger = new RecordingLogger<FixAllCommand>();
        var command = NewFixAll(asset, logger);
        command.CommandArgumentValue.ParseLayer(["-y"]);

        await command.Execute();

        A.CallTo(() => asset.ImportFromCatalogBatchAsync("sbeg",
                A<ImportFromCatalogBatchRequestDto>.That.Matches(r =>
                    !r.ForceBreakingUpdate && r.ModelIds.SequenceEqual(new[] { "Other-1.1.0" }))))
            .MustHaveHappenedOnceExactly();
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Energy-1.1.0"));
    }

    [Fact]
    public async Task FixAll_BlockedSetWithNoModelToLeaveOut_FailsAndImportsNothing()
    {
        var asset = AssetClient();
        A.CallTo(() => asset.GetLibraryStatusAsync(A<string>._)).Returns(new CkModelLibraryStatusResponseDto
        {
            Items = [Row("Other", "1.0.0", "1.1.0", blocked: false)]
        });
        Resolves(asset, new BatchDependencyResolutionResponseDto
        {
            ModelsToImport = ["Other-1.1.0"], DependencyTrees = [Tree("Other-1.1.0")],
            IsBlocked = true, Blockers = [Blocker("Unrelated-9.0.0", "Basic-2.4.0")]
        });
        var command = NewFixAll(asset, new RecordingLogger<FixAllCommand>());
        command.CommandArgumentValue.ParseLayer(["-y"]);

        var ex = await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Contains("never forces", ex.Message);
        A.CallTo(() => asset.ImportFromCatalogBatchAsync(A<string>._, A<ImportFromCatalogBatchRequestDto>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task FixAll_ServiceRefuses409_FailsWithoutForcing()
    {
        var asset = AssetClient();
        A.CallTo(() => asset.GetLibraryStatusAsync(A<string>._)).Returns(new CkModelLibraryStatusResponseDto
        {
            Items = [Row("Other", "1.0.0", "1.1.0", blocked: false)]
        });
        Resolves(asset, new BatchDependencyResolutionResponseDto
        {
            ModelsToImport = ["Other-1.1.0"], DependencyTrees = [Tree("Other-1.1.0")]
        });
        A.CallTo(() => asset.ImportFromCatalogBatchAsync(A<string>._, A<ImportFromCatalogBatchRequestDto>._))
            .Throws(new CkUpgradeBlockedException("d", ["Other-1.1.0"], [Blocker("Other-1.1.0", "Basic-2.4.0")]));
        var command = NewFixAll(asset, new RecordingLogger<FixAllCommand>());
        command.CommandArgumentValue.ParseLayer(["-y"]);

        var ex = await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Contains("never forces", ex.Message);
        A.CallTo(() => asset.ImportFromCatalogBatchAsync(A<string>._,
            A<ImportFromCatalogBatchRequestDto>.That.Matches(r => r.ForceBreakingUpdate))).MustNotHaveHappened();
    }
}
