using FakeItEasy;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Models;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.CkModelCatalog;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

public sealed class ImportConstructionKitModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("importck").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public async Task Execute_RejectsEmptyCk_BeforeAnythingIsUploaded()
    {
        var assetClient = A.Fake<IAssetServicesClient>();
        var path = Path.Combine(_dir, "empty.yaml");
        File.WriteAllText(path, "dependencies:\n- Basic\nmodelId: AccountStatement\n");
        var command = NewCommand(assetClient);
        command.CommandArgumentValue.ParseLayer(["-f", path]);

        await Assert.ThrowsAsync<ToolException>(command.Execute);

        A.CallTo(() => assetClient.ImportCkModelAsync(A<string>._, A<string>._, A<bool>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Execute_FailsForAMissingFile_BeforeAnythingIsUploaded()
    {
        var assetClient = A.Fake<IAssetServicesClient>();
        var path = Path.Combine(_dir, "does-not-exist.yaml");
        var command = NewCommand(assetClient);
        command.CommandArgumentValue.ParseLayer(["-f", path]);

        var exception = await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Contains(path, exception.Message);
        A.CallTo(() => assetClient.ImportCkModelAsync(A<string>._, A<string>._, A<bool>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Execute_UploadsAValidCk()
    {
        var assetClient = A.Fake<IAssetServicesClient>();
        A.CallTo(() => assetClient.ImportCkModelAsync(A<string>._, A<string>._, A<bool>._)).Returns("job1");
        var path = Path.Combine(_dir, "model.yaml");
        File.WriteAllText(path, "modelId: M-1.0.0\ntypes:\n- typeId: T\n");
        var command = NewCommand(assetClient);
        command.CommandArgumentValue.ParseLayer(["-f", path]);

        await command.Execute();

        A.CallTo(() => assetClient.ImportCkModelAsync("ab5470ec", path, false)).MustHaveHappenedOnceExactly();
    }

    private static CkUpgradeBlockerDto SystemBlocker() => new()
    {
        Dependent = "System.Identity-2.26.0", Update = "System-2.8.0", Kind = "ElementRemoved",
        Subject = "System@2/DisplayName-1", Code = "CK_UPGRADE_ELEMENT_REMOVED",
        Message = "System.Identity 2.26.0 uses System@2/DisplayName-1, which System-2.8.0 does not define"
    };

    private string BreakingFile()
    {
        var path = Path.Combine(_dir, "ck-system-2.yaml");
        File.WriteAllText(path, "modelId: System-2.8.0\ntypes:\n- typeId: T\n");
        return path;
    }

    [Fact]
    public async Task Execute_RefusedByTheGuard_PrintsTheBlockers_AndFailsWithCkUpgradeBlocked()
    {
        var assetClient = A.Fake<IAssetServicesClient>();
        A.CallTo(() => assetClient.ImportCkModelAsync(A<string>._, A<string>._, A<bool>._))
            .ThrowsAsync(new CkUpgradeBlockedException("detail", ["System-2.8.0"], [SystemBlocker()]));
        var logger = new RecordingLogger<ImportConstructionKitModel>();
        var command = new ImportConstructionKitModel(logger,
            Options.Create(new OctoToolOptions { TenantId = "ab5470ec" }), assetClient, A.Fake<IBotServicesClient>(),
            A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(["-f", BreakingFile()]);

        var ex = await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Contains("CK_UPGRADE_BLOCKED", ex.Message);
        Assert.Contains("--forceBreakingUpdate", ex.Message);
        Assert.Contains("System-2.8.0", ex.Message);
        Assert.Contains(logger.Entries, e => e.Message.Contains("System.Identity-2.26.0") && e.Message.Contains("ElementRemoved"));
        A.CallTo(() => assetClient.ImportCkModelAsync(A<string>._, A<string>._, false)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Execute_WithForceBreakingUpdate_SendsTheOverride_AndWarns()
    {
        var assetClient = A.Fake<IAssetServicesClient>();
        A.CallTo(() => assetClient.ImportCkModelAsync(A<string>._, A<string>._, A<bool>._)).Returns("job1");
        var logger = new RecordingLogger<ImportConstructionKitModel>();
        var command = new ImportConstructionKitModel(logger,
            Options.Create(new OctoToolOptions { TenantId = "ab5470ec" }), assetClient, A.Fake<IBotServicesClient>(),
            A.Fake<IAuthenticationService>());
        var path = BreakingFile();
        command.CommandArgumentValue.ParseLayer(["-f", path, "--forceBreakingUpdate"]);

        await command.Execute();

        A.CallTo(() => assetClient.ImportCkModelAsync("ab5470ec", path, true)).MustHaveHappenedOnceExactly();
        Assert.Contains(logger.Entries, e => e.Message.Contains("--forceBreakingUpdate"));
    }

    private static ImportConstructionKitModel NewCommand(IAssetServicesClient assetClient) =>
        new(NullLogger<ImportConstructionKitModel>.Instance,
            Options.Create(new OctoToolOptions { TenantId = "ab5470ec" }), assetClient,
            A.Fake<IBotServicesClient>(), A.Fake<IAuthenticationService>());
}
