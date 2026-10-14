using FakeItEasy;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.CkModelLibraries;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.CkModelCatalog;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
/// AB#6674: LibraryStatus prints the reason a CK model is ResolveFailed in its own REASON column.
/// </summary>
public sealed class LibraryStatusCommandTests
{
    private const string Reason = "Unmet dependency: Base-[1.0,2.0) (floor 1.1.0): installed Base-1.0.0";

    [Fact]
    public async Task Execute_PrintsTheReasonOfAResolveFailedModel_AndDashForTheOthers()
    {
        var client = A.Fake<IAssetServicesClient>();
        A.CallTo(() => client.GetLibraryStatusAsync(A<string>._)).Returns(new CkModelLibraryStatusResponseDto
        {
            Items =
            [
                new CkModelLibraryStatusItemDto
                {
                    Name = "Dep", InstalledVersion = "1.0.0", ModelState = "ResolveFailed", NeedsAction = true,
                    ResolveFailedReason = Reason
                },
                new CkModelLibraryStatusItemDto { Name = "Healthy", InstalledVersion = "1.0.0", ModelState = "Available" }
            ],
            ModelsNeedingActionCount = 1
        });
        var logger = new RecordingLogger<LibraryStatusCommand>();
        var command = new LibraryStatusCommand(logger, Options.Create(new OctoToolOptions { TenantId = "tenant" }), client,
            A.Fake<IAuthenticationService>());

        await command.Execute();

        Assert.Contains(logger.Entries, e => e.Message.Contains("REASON"));
        var failed = Assert.Single(logger.Entries, e => e.Message.StartsWith("Dep "));
        Assert.EndsWith(Reason, failed.Message);
        var healthy = Assert.Single(logger.Entries, e => e.Message.StartsWith("Healthy "));
        Assert.EndsWith(" -", healthy.Message);
    }
}
