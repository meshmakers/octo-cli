using FakeItEasy;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.CkModelLibraries;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.CkModelCatalog;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>AB#6869: CheckUpgrade lists what the offered update would break (tenant upgrade guard, AB#6868).</summary>
public sealed class CheckUpgradeCommandTests
{
    private static async Task<List<LogEntry>> Run(UpgradeCheckResponseDto response)
    {
        var client = A.Fake<IAssetServicesClient>();
        A.CallTo(() => client.CheckUpgradeAsync(A<string>._, A<ImportFromCatalogRequestDto>._)).Returns(response);
        var logger = new RecordingLogger<CheckUpgradeCommand>();
        var command = new CheckUpgradeCommand(logger, Options.Create(new OctoToolOptions { TenantId = "tenant" }), client,
            A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(["-cn", "PublicGitHubCatalog", "-m", "Basic-2.5.0"]);
        await command.Execute();
        return logger.Entries;
    }

    private static UpgradeCheckResponseDto Response(bool blocked) => new()
    {
        ModelName = "Basic", InstalledVersion = "2.4.0", TargetVersion = "2.5.0", UpgradeNeeded = true,
        MigrationPathAvailable = true, IsBlocked = blocked,
        Blockers = blocked
            ?
            [
                new CkUpgradeBlockerDto
                {
                    Dependent = "EnergyCommunity.Registration-2.8.0", Update = "Basic-2.5.0", Kind = "ExactPin",
                    Subject = "Basic-2.4.0", Message = "exact pin Basic-2.4.0: Basic-2.5.0 replaces it",
                    ResolveHint = "update EnergyCommunity.Registration to 2.10.0 first", DependentIsInUpdateSet = false
                },
                new CkUpgradeBlockerDto
                {
                    Dependent = "EnergyCommunity.Billing-1.3.0", Kind = "ExactPin", Subject = "Basic-2.4.0",
                    Message = "exact pin Basic-2.4.0: Basic-2.5.0 replaces it"
                }
            ]
            : []
    };

    [Fact]
    public async Task Execute_Blocked_ListsOneLinePerBlockerAfterTheUnchangedFields()
    {
        var messages = (await Run(Response(true))).Select(e => e.Message).ToList();

        Assert.Equal("Breaking Changes:   False", messages[5]);
        Assert.Equal("Would break:", messages[6]);
        Assert.Equal("  - EnergyCommunity.Registration-2.8.0: exact pin Basic-2.4.0: Basic-2.5.0 replaces it", messages[7]);
        Assert.Equal("      resolve: update EnergyCommunity.Registration to 2.10.0 first", messages[8]);
        Assert.Equal("  - EnergyCommunity.Billing-1.3.0: exact pin Basic-2.4.0: Basic-2.5.0 replaces it", messages[9]);
        Assert.Equal(10, messages.Count);
    }

    [Fact]
    public async Task Execute_NotBlocked_OutputIsUnchanged()
    {
        var messages = (await Run(Response(false))).Select(e => e.Message).ToList();

        Assert.Equal(6, messages.Count);
        Assert.DoesNotContain("Would break:", messages);
    }

    [Fact]
    public async Task Execute_OlderServerWithoutTheMembers_OutputIsUnchanged()
    {
        var response = System.Text.Json.JsonSerializer.Deserialize<UpgradeCheckResponseDto>(
            """{"modelName":"Basic","installedVersion":"2.0.0","targetVersion":"2.1.0","upgradeNeeded":true}""",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

        var messages = (await Run(response)).Select(e => e.Message).ToList();

        Assert.Equal(6, messages.Count);
    }
}
