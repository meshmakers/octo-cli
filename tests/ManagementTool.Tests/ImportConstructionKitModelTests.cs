using FakeItEasy;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Models;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
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

        A.CallTo(() => assetClient.ImportCkModelAsync(A<string>._, A<string>._)).MustNotHaveHappened();
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
        A.CallTo(() => assetClient.ImportCkModelAsync(A<string>._, A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Execute_UploadsAValidCk()
    {
        var assetClient = A.Fake<IAssetServicesClient>();
        A.CallTo(() => assetClient.ImportCkModelAsync(A<string>._, A<string>._)).Returns("job1");
        var path = Path.Combine(_dir, "model.yaml");
        File.WriteAllText(path, "modelId: M-1.0.0\ntypes:\n- typeId: T\n");
        var command = NewCommand(assetClient);
        command.CommandArgumentValue.ParseLayer(["-f", path]);

        await command.Execute();

        A.CallTo(() => assetClient.ImportCkModelAsync("ab5470ec", path)).MustHaveHappenedOnceExactly();
    }

    private static ImportConstructionKitModel NewCommand(IAssetServicesClient assetClient) =>
        new(NullLogger<ImportConstructionKitModel>.Instance,
            Options.Create(new OctoToolOptions { TenantId = "ab5470ec" }), assetClient,
            A.Fake<IBotServicesClient>(), A.Fake<IAuthenticationService>());
}
