using FakeItEasy;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.CkModelLibraries;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.CkModelCatalog;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using System.Text.Json;
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

    private static CkUpgradeBlockerDto Blocker(string dependent, string message, string? hint = null) => new()
    {
        Dependent = dependent, Update = "Basic-2.5.0", Kind = "ExactPin", Subject = "Basic-2.4.0",
        Code = "CK_UPGRADE_EXACT_PIN", Message = message, SuggestedDependentUpdate = hint == null ? null : dependent,
        ResolveHint = hint
    };

    private static async Task<List<LogEntry>> Run(CkModelLibraryStatusResponseDto response, params string[] args)
    {
        var client = A.Fake<IAssetServicesClient>();
        A.CallTo(() => client.GetLibraryStatusAsync(A<string>._)).Returns(response);
        var logger = new RecordingLogger<LibraryStatusCommand>();
        var command = new LibraryStatusCommand(logger, Options.Create(new OctoToolOptions { TenantId = "tenant" }), client,
            A.Fake<IAuthenticationService>());
        if (args.Length > 0)
            command.CommandArgumentValue.ParseLayer(args);
        await command.Execute();
        return logger.Entries;
    }

    private static CkModelLibraryStatusResponseDto SbegCase() => new()
    {
        Items =
        [
            new CkModelLibraryStatusItemDto
            {
                Name = "Basic", InstalledVersion = "2.4.0", CatalogVersion = "2.5.0", ModelState = "Available",
                HasUpdate = true, NeedsAction = false, IsBlocked = true,
                Blockers =
                [
                    Blocker("EnergyCommunity.Registration-2.8.0", "exact pin Basic-2.4.0: Basic-2.5.0 replaces it",
                        "update EnergyCommunity.Registration to 2.10.0 first"),
                    Blocker("EnergyCommunity.Billing-1.3.0", "exact pin Basic-2.4.0: Basic-2.5.0 replaces it")
                ]
            },
            new CkModelLibraryStatusItemDto
            {
                Name = "Additive", InstalledVersion = "1.0.0", CatalogVersion = "1.1.0", ModelState = "Available",
                HasUpdate = true, NeedsAction = true
            },
            new CkModelLibraryStatusItemDto
            {
                Name = "Broken", InstalledVersion = "1.0.0", ModelState = "ResolveFailed", NeedsAction = true,
                ResolveFailedReason = "Unmet dependency"
            },
            new CkModelLibraryStatusItemDto
            {
                Name = "Old", InstalledVersion = "1.0.0", CatalogVersion = "2.0.0", IsCompatible = false
            },
            new CkModelLibraryStatusItemDto { Name = "Fresh", CatalogVersion = "1.0.0" }
        ],
        ModelsNeedingActionCount = 2
    };

    [Fact]
    public async Task Execute_ShowsBlockedActionReasonSummaryAndWouldBreakSection_ForTheSbegCase()
    {
        var entries = await Run(SbegCase());

        Assert.Contains(entries, e => e.Message.StartsWith("5 model(s) (2 need action, 1 blocked):"));
        var basic = Assert.Single(entries, e => e.Message.StartsWith("Basic       "));
        Assert.Contains(" Blocked ", basic.Message);
        Assert.Contains("EnergyCommunity.Registration-2.8.0: exact pin Basic-2.4.0", basic.Message);
        Assert.Contains("(+1 more)", basic.Message);
        Assert.EndsWith("; update EnergyCommunity.Registration to 2.10.0 first", basic.Message);

        var messages = entries.Select(e => e.Message).ToList();
        var section = messages.IndexOf("Would break:");
        Assert.True(section > 0);
        Assert.Equal("Basic 2.4.0 -> 2.5.0", messages[section + 1]);
        Assert.Contains(messages, m => m.StartsWith("  - EnergyCommunity.Registration-2.8.0: exact pin"));
        Assert.Contains(messages, m => m == "      resolve: update EnergyCommunity.Registration to 2.10.0 first");
        Assert.Contains(messages, m => m.StartsWith("  - EnergyCommunity.Billing-1.3.0: exact pin"));
    }

    [Fact]
    public async Task Execute_KeepsUpdateIncompatibleFixAndInstall_NextToBlocked()
    {
        var entries = await Run(SbegCase());

        Assert.Contains(" Update ", Assert.Single(entries, e => e.Message.StartsWith("Additive ")).Message);
        Assert.Contains(" Fix ", Assert.Single(entries, e => e.Message.StartsWith("Broken ")).Message);
        Assert.Contains(" Incompatible ", Assert.Single(entries, e => e.Message.StartsWith("Old ")).Message);
        Assert.Contains(" Install ", Assert.Single(entries, e => e.Message.StartsWith("Fresh ")).Message);
    }

    [Fact]
    public async Task Execute_NeedsAction_DoesNotListABlockedRow()
    {
        var entries = await Run(SbegCase(), "-na");

        Assert.DoesNotContain(entries, e => e.Message.StartsWith("Basic       "));
        Assert.DoesNotContain(entries, e => e.Message == "Would break:");
        Assert.Contains(entries, e => e.Message.StartsWith("Additive "));
    }

    [Fact]
    public async Task Execute_WithoutBlockedRows_PrintsTheUnchangedSummaryAndNoSection()
    {
        var response = SbegCase();
        response.Items.RemoveAt(0);
        var entries = await Run(response);

        Assert.Contains(entries, e => e.Message == "4 model(s) (2 need action):");
        Assert.DoesNotContain(entries, e => e.Message == "Would break:");
    }

    [Fact]
    public async Task Execute_AnswerOfAnOlderServer_WithoutTheNewMembers_BehavesAsBefore()
    {
        const string json = """
            {"items":[{"name":"Basic","installedVersion":"2.0.0","catalogVersion":"2.1.0","modelState":"Available",
            "hasUpdate":true,"needsAction":true,"isCompatible":true}],"modelsNeedingActionCount":1}
            """;
        var response = JsonSerializer.Deserialize<CkModelLibraryStatusResponseDto>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var entries = await Run(response);

        Assert.Contains(entries, e => e.Message == "1 model(s) (1 need action):");
        Assert.Contains(" Update ", Assert.Single(entries, e => e.Message.StartsWith("Basic       ")).Message);
        Assert.DoesNotContain(entries, e => e.Message == "Would break:");
    }

    [Fact]
    public async Task Execute_BlockedRowWithNullBlockersOrEmptyMessage_DoesNotThrow()
    {
        var response = new CkModelLibraryStatusResponseDto
        {
            Items =
            [
                new CkModelLibraryStatusItemDto { Name = "A", InstalledVersion = "1.0.0", IsBlocked = true, Blockers = null! },
                new CkModelLibraryStatusItemDto
                {
                    Name = "B", InstalledVersion = "1.0.0", IsBlocked = true,
                    Blockers = [new CkUpgradeBlockerDto { Dependent = "X-1.0.0", Kind = "ElementRemoved", Subject = "Foo" }]
                }
            ]
        };
        var entries = await Run(response);

        Assert.Contains("Would break an installed model", Assert.Single(entries, e => e.Message.StartsWith("A          ")).Message);
        Assert.Contains("X-1.0.0: ElementRemoved Foo", Assert.Single(entries, e => e.Message.StartsWith("B          ")).Message);
    }

    [Fact]
    public void Dto_ThatIsBlocked_RoundTripsAllNewMembersThroughJson()
    {
        var item = SbegCase().Items[0];
        var json = JsonSerializer.Serialize(item, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"isBlocked\":true", json);
        Assert.Contains("\"resolveHint\":\"update EnergyCommunity.Registration to 2.10.0 first\"", json);
        var back = JsonSerializer.Deserialize<CkModelLibraryStatusItemDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(2, back.Blockers.Count);
    }
}
