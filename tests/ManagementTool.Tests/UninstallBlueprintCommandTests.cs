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
///     AB#8966: UninstallBlueprint --dry-run calls the dry run (never the uninstall, no confirmation) and both modes list the
///     kept tenant-owned seed entities with the reason.
/// </summary>
public sealed class UninstallBlueprintCommandTests
{
    private readonly IAssetServicesClient _client = A.Fake<IAssetServicesClient>();
    private readonly IConfirmationService _confirmation = A.Fake<IConfirmationService>();
    private readonly IConsoleService _console = A.Fake<IConsoleService>();
    private readonly RecordingLogger<UninstallBlueprint> _logger = new();

    private static BlueprintUninstallResultDto Result(bool dryRun) => new()
    {
        Success = true,
        DryRun = dryRun,
        EntitiesDeleted = 1,
        DeletedEntities =
        [
            new BlueprintUninstallDeletedEntityDto
            {
                Key = "page-a", CkTypeId = "System.Ai/KnowledgePage", EntityId = "aa0000000000000000000001", TenantOwned = true
            }
        ],
        TenantOwnedKept =
        [
            new BlueprintTenantOwnedKeptEntityDto
            {
                Key = "page-b", CkTypeId = "System.Ai/KnowledgePage", EntityId = "aa0000000000000000000002", Reason = "Edited"
            },
            new BlueprintTenantOwnedKeptEntityDto
            {
                Key = "page-b@1", CkTypeId = "System.Ai/KnowledgePageVersion", EntityId = "aa0000000000000000000003",
                Reason = "Referenced", ReferencedBy = ["aa0000000000000000000002"]
            }
        ]
    };

    private UninstallBlueprint NewCommand() =>
        new(_logger, _console, _confirmation, Options.Create(new OctoToolOptions { TenantId = "acme" }), _client,
            A.Fake<IAuthenticationService>());

    private string Infos => string.Join('\n',
        _logger.Entries.Where(e => e.Level == LogLevel.Information).Select(e => e.Message));

    [Fact]
    public async Task DryRun_CallsThePreview_NeverTheUninstall_AndAsksNothing()
    {
        A.CallTo(() => _client.PreviewUninstallBlueprintAsync("acme", "System.Ai.Builder", true)).Returns(Result(dryRun: true));
        var command = NewCommand();
        command.CommandArgumentValue.ParseLayer(["-n", "System.Ai.Builder", "--cascade", "--dry-run"]);

        await command.Execute();

        A.CallTo(() => _client.UninstallBlueprintAsync(A<string>._, A<string>._, A<bool>._)).MustNotHaveHappened();
        A.CallTo(() => _confirmation.Confirm(A<string>._)).MustNotHaveHappened();
        Assert.Contains("1 entities would be erased, 2 tenant-owned seed entities kept", Infos);
        Assert.Contains("kept (Edited): System.Ai/KnowledgePage page-b aa0000000000000000000002", Infos);
        Assert.Contains("kept (Referenced): System.Ai/KnowledgePageVersion page-b@1 aa0000000000000000000003 referenced by aa0000000000000000000002", Infos);
        A.CallTo(() => _console.WriteLine(A<string>.That.Contains("\"DryRun\": true"))).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Uninstall_ListsTheKeptEntitiesWithTheReason()
    {
        A.CallTo(() => _client.UninstallBlueprintAsync("acme", "System.Ai.Builder", false)).Returns(Result(dryRun: false));
        var command = NewCommand();
        command.CommandArgumentValue.ParseLayer(["-n", "System.Ai.Builder", "-y"]);

        await command.Execute();

        A.CallTo(() => _client.PreviewUninstallBlueprintAsync(A<string>._, A<string>._, A<bool>._)).MustNotHaveHappened();
        Assert.Contains("Uninstall complete: 1 entities erased, 2 tenant-owned seed entities kept", Infos);
        Assert.Contains("kept (Edited)", Infos);
        A.CallTo(() => _console.WriteLine(A<string>.That.Contains("TenantOwnedKept"))).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task DryRun_BlockedByDependents_NamesThemAndTheCascadeOption()
    {
        A.CallTo(() => _client.PreviewUninstallBlueprintAsync("acme", "System.Ai.Builder", false))
            .Returns(new BlueprintUninstallResultDto { Success = false, DryRun = true, BlockingDependents = ["Addon-1.0.0"] });
        var command = NewCommand();
        command.CommandArgumentValue.ParseLayer(["-n", "System.Ai.Builder", "--dry-run"]);

        await command.Execute();

        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("Addon-1.0.0")
                                              && e.Message.Contains("--cascade"));
    }

    [Fact]
    public async Task Uninstall_WithoutYes_StillAsksForConfirmation()
    {
        A.CallTo(() => _confirmation.Confirm(A<string>._)).Returns(false);
        var command = NewCommand();
        command.CommandArgumentValue.ParseLayer(["-n", "System.Ai.Builder"]);

        await Assert.ThrowsAsync<ToolException>(command.Execute);

        A.CallTo(() => _client.UninstallBlueprintAsync(A<string>._, A<string>._, A<bool>._)).MustNotHaveHappened();
    }
}
