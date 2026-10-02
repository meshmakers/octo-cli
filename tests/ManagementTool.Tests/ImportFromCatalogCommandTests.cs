using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.CkModelLibraries;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.CkModelCatalog;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

public sealed class ImportFromCatalogCommandTests
{
    private const string JobId = "6abf6b40466e54c8b02451eb";

    [Fact]
    public async Task Execute_WithWait_FailsWithTheJobError_WhenTheImportJobFails()
    {
        var botClient = NewBotClient("Failed", "Sequence contains more than one matching element");
        var command = NewCommand(NewAssetClient(), botClient);
        command.CommandArgumentValue.ParseLayer(["-cn", "PublicGitHubCatalog", "-m", "EnergyCommunity-4.6.0", "-w"]);

        var exception = await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Contains(JobId, exception.Message);
        Assert.Contains("Sequence contains more than one matching element", exception.Message);
    }

    [Fact]
    public async Task Execute_WithWait_ReturnsAfterTheImportJobSucceeded()
    {
        var botClient = NewBotClient("Succeeded", null);
        var command = NewCommand(NewAssetClient(), botClient);
        command.CommandArgumentValue.ParseLayer(["-cn", "PublicGitHubCatalog", "-m", "Industry.Basic-2.3.0", "-w"]);

        await command.Execute();

        A.CallTo(() => botClient.GetImportJobStatus(JobId)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Execute_WithoutWait_DoesNotPollTheJob()
    {
        var botClient = NewBotClient("Failed", "never read");
        var command = NewCommand(NewAssetClient(), botClient);
        command.CommandArgumentValue.ParseLayer(["-cn", "PublicGitHubCatalog", "-m", "Industry.Basic-2.3.0"]);

        await command.Execute();

        A.CallTo(() => botClient.GetImportJobStatus(A<string>._)).MustNotHaveHappened();
    }

    private static IAssetServicesClient NewAssetClient()
    {
        var assetClient = A.Fake<IAssetServicesClient>();
        A.CallTo(() => assetClient.ResolveDependenciesBatchAsync(A<string>._, A<List<ImportFromCatalogRequestDto>>._))
            .Returns(new BatchDependencyResolutionResponseDto { ModelsToImport = ["Basic.Energy-1.7.0", "EnergyCommunity-4.6.0"] });
        A.CallTo(() => assetClient.ImportFromCatalogBatchAsync(A<string>._, A<ImportFromCatalogBatchRequestDto>._))
            .Returns(new BatchImportResponseDto { JobId = JobId });
        return assetClient;
    }

    private static IBotServicesClient NewBotClient(string status, string? errorMessage)
    {
        var botClient = A.Fake<IBotServicesClient>();
        A.CallTo(() => botClient.GetImportJobStatus(JobId))
            .Returns(new JobDto { Id = JobId, Status = status, ErrorMessage = errorMessage });
        return botClient;
    }

    private static ImportFromCatalogCommand NewCommand(IAssetServicesClient assetClient, IBotServicesClient botClient) =>
        new(NullLogger<ImportFromCatalogCommand>.Instance,
            Options.Create(new OctoToolOptions { TenantId = "ab5470ec" }), assetClient, botClient,
            A.Fake<IAuthenticationService>());
}
