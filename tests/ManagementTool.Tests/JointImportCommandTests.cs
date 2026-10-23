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
///     AB#7376: upgrade of a group of mutually exact-pinned CK models in one guarded import - the octo-mesh-energy group
///     of staging-1 (Basic 2.4.0, Industry.Basic 2.5.0, Industry.Energy 3.5.0, Industry.Manufacturing 2.5.0,
///     Industry.Maintenance 2.4.0, Industry.Fluid 2.4.0). Industry.Basic-2.6.0 alone breaks Energy/Fluid/Maintenance/
///     Manufacturing (exact pin Industry.Basic-2.5.0); Industry.Energy-3.6.0 alone does not pull in Industry.Basic-2.6.0.
/// </summary>
public sealed class JointImportCommandTests
{
    private const string JobId = "job-7376";
    private const string Catalog = "PublicGitHubCatalog";

    private static readonly string[] Dependents =
    [
        "Industry.Energy-3.6.0", "Industry.Fluid-2.5.0", "Industry.Maintenance-2.5.0", "Industry.Manufacturing-2.6.0"
    ];

    private static DependencyResolutionItemDto Item(string modelId, string action, params DependencyResolutionItemDto[] deps) => new()
    {
        ModelId = modelId, Name = JointImportPlanner.NameOf(modelId), Action = action, Dependencies = deps.ToList()
    };

    // The tree the service builds for a root: dependencies below it; Industry.Basic only as the installed 2.5.0 (action none).
    private static DependencyResolutionResponseDto DependentTree(string modelId) => new()
    {
        RootModel = Item(modelId, "update",
            Item("Industry.Basic-2.5.0", "none"),
            Item("Basic-2.5.0", "update"))
    };

    private static DependencyResolutionResponseDto IndustryBasicTree() => new()
    {
        RootModel = Item("Industry.Basic-2.6.0", "update", Item("Basic-2.5.0", "update"))
    };

    /// <summary>Resolver answer for the roots in request order: trees concatenated, a model once (first occurrence).</summary>
    private static BatchDependencyResolutionResponseDto Resolve(IEnumerable<string> roots)
    {
        var trees = new List<DependencyResolutionResponseDto>();
        var models = new List<string>();
        foreach (var root in roots)
        {
            var tree = root switch
            {
                "Industry.Basic-2.6.0" => IndustryBasicTree(),
                "Basic-2.5.0" => new DependencyResolutionResponseDto { RootModel = Item("Basic-2.5.0", "update") },
                _ => DependentTree(root)
            };
            trees.Add(tree);
            AddFlattened(tree.RootModel, models);
        }

        return new BatchDependencyResolutionResponseDto { ModelsToImport = models, DependencyTrees = trees };
    }

    private static void AddFlattened(DependencyResolutionItemDto item, List<string> models)
    {
        foreach (var dependency in item.Dependencies)
            AddFlattened(dependency, models);
        if (item.Action != "none" && !models.Contains(item.ModelId))
            models.Add(item.ModelId);
    }

    private static CkUpgradeBlockerDto PinBlocker(string dependent, string suggestion) => new()
    {
        Dependent = dependent, Update = "Industry.Basic-2.6.0", Kind = "ExactPin", Subject = "Industry.Basic-2.5.0",
        Code = "CK_UPGRADE_EXACT_PIN", Message = "exact pin Industry.Basic-2.5.0: Industry.Basic-2.6.0 replaces it",
        SuggestedDependentUpdate = suggestion, ResolveHint = $"update {suggestion} first"
    };

    private static IAssetServicesClient AssetClient()
    {
        var asset = A.Fake<IAssetServicesClient>();
        A.CallTo(() => asset.ImportFromCatalogBatchAsync(A<string>._, A<ImportFromCatalogBatchRequestDto>._))
            .Returns(new BatchImportResponseDto { JobId = JobId });
        return asset;
    }

    private static IBotServicesClient BotClient()
    {
        var bot = A.Fake<IBotServicesClient>();
        A.CallTo(() => bot.GetImportJobStatus(JobId)).Returns(new JobDto { Id = JobId, Status = "Succeeded" });
        return bot;
    }

    private static ImportFromCatalogCommand NewImport(IAssetServicesClient asset, RecordingLogger<ImportFromCatalogCommand> logger) =>
        new(logger, Options.Create(new OctoToolOptions { TenantId = "energy" }), asset, BotClient(),
            A.Fake<IAuthenticationService>());

    private static FixAllCommand NewFixAll(IAssetServicesClient asset, RecordingLogger<FixAllCommand> logger) =>
        new(logger, Options.Create(new OctoToolOptions { TenantId = "energy" }), asset, BotClient(),
            A.Fake<IAuthenticationService>(), A.Fake<IConfirmationService>());

    private static void AssertDependencyOrder(List<string> ids)
    {
        Assert.Equal(6, ids.Count); // Basic, Industry.Basic and the four dependents
        var basic = ids.IndexOf("Basic-2.5.0");
        var industryBasic = ids.IndexOf("Industry.Basic-2.6.0");
        Assert.True(basic >= 0 && industryBasic >= 0);
        Assert.True(basic < industryBasic, "Basic before Industry.Basic");
        foreach (var dependent in Dependents)
        {
            Assert.True(ids.IndexOf(dependent) > industryBasic, $"{dependent} after Industry.Basic");
        }
    }

    [Fact]
    public async Task ImportFromCatalog_JointSetInWrongArgumentOrder_SendsOneBatchInDependencyOrder_WithoutForce()
    {
        var asset = AssetClient();
        A.CallTo(() => asset.ResolveDependenciesBatchAsync(A<string>._, A<List<ImportFromCatalogRequestDto>>._))
            .ReturnsLazily((string _, List<ImportFromCatalogRequestDto> r) => Resolve(r.Select(x => x.ModelId)));
        var command = NewImport(asset, new RecordingLogger<ImportFromCatalogCommand>());
        command.CommandArgumentValue.ParseLayer(["-cn", Catalog, "-m",
            "Industry.Energy-3.6.0,Industry.Fluid-2.5.0,Industry.Maintenance-2.5.0,Industry.Manufacturing-2.6.0,Industry.Basic-2.6.0", "-w"]);

        await command.Execute();

        // One resolve with all five roots, one batch, one job.
        A.CallTo(() => asset.ResolveDependenciesBatchAsync("energy",
                A<List<ImportFromCatalogRequestDto>>.That.Matches(r => r.Count == 5 && r.All(x => x.CatalogName == Catalog))))
            .MustHaveHappenedOnceExactly();
        // The resolver order would be Basic, Energy, Fluid, Maintenance, Manufacturing, Industry.Basic: wrong.
        var resolved = Resolve(["Industry.Energy-3.6.0", "Industry.Basic-2.6.0"]).ModelsToImport;
        Assert.True(resolved.IndexOf("Industry.Energy-3.6.0") < resolved.IndexOf("Industry.Basic-2.6.0"));

        A.CallTo(() => asset.ImportFromCatalogBatchAsync("energy",
                A<ImportFromCatalogBatchRequestDto>.That.Matches(r => !r.ForceBreakingUpdate && IsInDependencyOrder(r.ModelIds))))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => asset.ImportFromCatalogBatchAsync(A<string>._, A<ImportFromCatalogBatchRequestDto>._))
            .MustHaveHappenedOnceExactly();
    }

    private static bool IsInDependencyOrder(List<string> ids)
    {
        AssertDependencyOrder(ids);
        return true;
    }

    [Fact]
    public async Task ImportFromCatalog_SingleModel_BehavesAsBefore()
    {
        var asset = AssetClient();
        A.CallTo(() => asset.ResolveDependenciesBatchAsync(A<string>._, A<List<ImportFromCatalogRequestDto>>._))
            .Returns(new BatchDependencyResolutionResponseDto { ModelsToImport = ["Basic.Energy-1.7.0", "EnergyCommunity-4.6.0"] });
        var command = NewImport(asset, new RecordingLogger<ImportFromCatalogCommand>());
        command.CommandArgumentValue.ParseLayer(["-cn", Catalog, "-m", "EnergyCommunity-4.6.0"]);

        await command.Execute();

        A.CallTo(() => asset.ResolveDependenciesBatchAsync("energy",
                A<List<ImportFromCatalogRequestDto>>.That.Matches(r => r.Count == 1 && r[0].ModelId == "EnergyCommunity-4.6.0")))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => asset.ImportFromCatalogBatchAsync("energy",
                A<ImportFromCatalogBatchRequestDto>.That.Matches(r =>
                    r.ModelIds.SequenceEqual(new[] { "Basic.Energy-1.7.0", "EnergyCommunity-4.6.0" }))))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ImportFromCatalog_BlockedSingleImport_ImportsNothing_AndPrintsTheJointCommand()
    {
        var asset = AssetClient();
        var blocked = Resolve(["Industry.Energy-3.6.0"]);
        blocked.IsBlocked = true;
        blocked.Blockers = [PinBlocker("Industry.Fluid-2.4.0", "Industry.Fluid-2.5.0"),
            PinBlocker("Industry.Maintenance-2.4.0", "Industry.Maintenance-2.5.0"),
            PinBlocker("Industry.Manufacturing-2.5.0", "Industry.Manufacturing-2.6.0"),
            PinBlocker("Industry.Energy-3.5.0", "Industry.Energy-3.6.0"),
            PinBlocker("Industry.Basic-2.5.0", "Industry.Basic-2.6.0")];
        A.CallTo(() => asset.ResolveDependenciesBatchAsync(A<string>._, A<List<ImportFromCatalogRequestDto>>._)).Returns(blocked);
        var logger = new RecordingLogger<ImportFromCatalogCommand>();
        var command = NewImport(asset, logger);
        command.CommandArgumentValue.ParseLayer(["-cn", Catalog, "-m", "Industry.Energy-3.6.0", "-w"]);

        var ex = await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Contains("CK_UPGRADE_BLOCKED", ex.Message);
        A.CallTo(() => asset.ImportFromCatalogBatchAsync(A<string>._, A<ImportFromCatalogBatchRequestDto>._))
            .MustNotHaveHappened();
        var joint = Assert.Single(logger.Entries, e => e.Message.Contains("octo-cli -c ImportFromCatalog"));
        Assert.Contains($"-cn {Catalog}", joint.Message);
        Assert.Contains("Industry.Basic-2.6.0", joint.Message);
        Assert.Contains("Industry.Energy-3.6.0", joint.Message);
        Assert.Contains("Industry.Fluid-2.5.0", joint.Message);
        Assert.Contains("Industry.Maintenance-2.5.0", joint.Message);
        Assert.Contains("Industry.Manufacturing-2.6.0", joint.Message);
        Assert.EndsWith("-w", joint.Message);
        // The requested model is not listed twice.
        Assert.Equal(1, joint.Message.Split("Industry.Energy-3.6.0").Length - 1);
    }

    [Fact]
    public async Task FixAll_SixBlockedRows_AreImportedTogetherInOneBatchInDependencyOrder_NeverForced()
    {
        var asset = AssetClient();
        A.CallTo(() => asset.GetLibraryStatusAsync(A<string>._)).Returns(new CkModelLibraryStatusResponseDto
        {
            Items =
            [
                // Deliberately the dependents before their dependencies: the resolver concatenates in this order.
                BlockedRow("Industry.Energy", "3.5.0", "3.6.0"), BlockedRow("Industry.Fluid", "2.4.0", "2.5.0"),
                BlockedRow("Industry.Maintenance", "2.4.0", "2.5.0"), BlockedRow("Industry.Manufacturing", "2.5.0", "2.6.0"),
                BlockedRow("Industry.Basic", "2.5.0", "2.6.0"), BlockedRow("Basic", "2.4.0", "2.5.0")
            ]
        });
        A.CallTo(() => asset.ResolveDependenciesBatchAsync(A<string>._, A<List<ImportFromCatalogRequestDto>>._))
            .ReturnsLazily((string _, List<ImportFromCatalogRequestDto> r) => Resolve(r.Select(x => x.ModelId)));
        var command = NewFixAll(asset, new RecordingLogger<FixAllCommand>());
        command.CommandArgumentValue.ParseLayer(["-y", "-w"]);

        await command.Execute();

        A.CallTo(() => asset.ResolveDependenciesBatchAsync("energy",
                A<List<ImportFromCatalogRequestDto>>.That.Matches(r => r.Count == 6)))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => asset.ImportFromCatalogBatchAsync("energy",
                A<ImportFromCatalogBatchRequestDto>.That.Matches(r => !r.ForceBreakingUpdate && IsInDependencyOrder(r.ModelIds))))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => asset.ImportFromCatalogBatchAsync(A<string>._, A<ImportFromCatalogBatchRequestDto>._))
            .MustHaveHappenedOnceExactly();
    }

    private static CkModelLibraryStatusItemDto BlockedRow(string name, string installed, string catalog) => new()
    {
        Name = name, InstalledVersion = installed, CatalogVersion = catalog, HasUpdate = true, NeedsAction = false,
        IsBlocked = true, CatalogName = Catalog, FullModelId = $"{name}-{catalog}",
        Blockers = [PinBlocker($"{name}-{installed}", $"{name}-{catalog}")]
    };

    [Fact]
    public void OrderByDependencies_IsStable_ForIndependentModels()
    {
        var resolution = new BatchDependencyResolutionResponseDto
        {
            ModelsToImport = ["B-1.0.0", "A-1.0.0", "C-1.0.0"],
            DependencyTrees = [new() { RootModel = Item("B-1.0.0", "update") }, new() { RootModel = Item("A-1.0.0", "update") },
                new() { RootModel = Item("C-1.0.0", "update") }]
        };

        Assert.Equal(["B-1.0.0", "A-1.0.0", "C-1.0.0"], JointImportPlanner.OrderByDependencies(resolution));
    }

    [Fact]
    public void BuildJointCommand_ReturnsNull_WhenTheBlockersSuggestNothingNew()
    {
        var blocker = new CkUpgradeBlockerDto { Dependent = "X-1.0.0", Update = "Y-2.0.0" };

        Assert.Null(JointImportPlanner.BuildJointCommand(Catalog, ["Y-2.0.0"], [blocker], false));
    }
}
