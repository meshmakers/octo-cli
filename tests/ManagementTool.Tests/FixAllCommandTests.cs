using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.CkModelLibraries;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.CkModelCatalog;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

public sealed class FixAllCommandTests
{
    private const string JobId = "6abf74c0466e54c8b0245209";

    [Fact]
    public async Task Execute_WithWait_FailsWithTheJobError_WhenTheImportJobFails()
    {
        var botClient = NewBotClient("Failed", "Sequence contains more than one matching element");
        var command = NewCommand(botClient, new RecordingLogger<FixAllCommand>());
        command.CommandArgumentValue.ParseLayer(["-y", "-w"]);

        var exception = await Assert.ThrowsAsync<ToolException>(command.Execute);

        Assert.Contains(JobId, exception.Message);
        Assert.Contains("Sequence contains more than one matching element", exception.Message);
    }

    [Fact]
    public async Task Execute_WithWait_ReturnsAfterTheImportJobSucceeded()
    {
        var botClient = NewBotClient("Succeeded", null);
        var command = NewCommand(botClient, new RecordingLogger<FixAllCommand>());
        command.CommandArgumentValue.ParseLayer(["-y", "-w"]);

        await command.Execute();

        A.CallTo(() => botClient.GetImportJobStatus(JobId)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Execute_WithoutWait_PrintsTheJobIdWithoutPollingIt()
    {
        var botClient = NewBotClient("Failed", "never read");
        var logger = new RecordingLogger<FixAllCommand>();
        var command = NewCommand(botClient, logger);
        command.CommandArgumentValue.ParseLayer(["-y"]);

        await command.Execute();

        A.CallTo(() => botClient.GetImportJobStatus(A<string>._)).MustNotHaveHappened();
        Assert.Contains(logger.Entries, e => e.Message.Contains(JobId));
    }

    private static IAssetServicesClient NewAssetClient()
    {
        var assetClient = A.Fake<IAssetServicesClient>();
        A.CallTo(() => assetClient.GetLibraryStatusAsync(A<string>._))
            .Returns(new CkModelLibraryStatusResponseDto
            {
                Items =
                [
                    new CkModelLibraryStatusItemDto
                    {
                        Name = "EnergyCommunity", InstalledVersion = "3.3.0", CatalogVersion = "4.6.0",
                        NeedsAction = true, CatalogName = "PublicGitHubCatalog", FullModelId = "EnergyCommunity-4.6.0"
                    }
                ]
            });
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

    private static FixAllCommand NewCommand(IBotServicesClient botClient, RecordingLogger<FixAllCommand> logger) =>
        new(logger, Options.Create(new OctoToolOptions { TenantId = "ab5470up" }), NewAssetClient(), botClient,
            A.Fake<IAuthenticationService>(), A.Fake<IConfirmationService>());
}
