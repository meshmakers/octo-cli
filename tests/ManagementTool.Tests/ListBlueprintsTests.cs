using FakeItEasy;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Blueprints;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Blueprints;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace ManagementTool.Tests;

/// <summary>
/// AB#5650: ListBlueprints used to fetch one default page (take=100) and print it as if it were the
/// whole catalog - test-2 reports 151 entries, so newer versions served by the private catalog were
/// invisible. Without -s/-t the command must page until the service-reported total is reached.
/// </summary>
public sealed class ListBlueprintsTests
{
    private const int CatalogSize = 151;

    [Fact]
    public async Task Execute_WithoutPaging_FetchesAllPages()
    {
        var catalog = BuildCatalog(CatalogSize);
        var serviceClient = NewServiceClient(catalog);
        var (console, lines) = NewRecordingConsole();
        var command = NewCommand(serviceClient, console);
        command.CommandArgumentValue.ParseLayer([]);

        await command.Execute();

        A.CallTo(() => serviceClient.ListBlueprintsAsync(0, ListBlueprints.PageSize)).MustHaveHappenedOnceExactly();
        A.CallTo(() => serviceClient.ListBlueprintsAsync(100, ListBlueprints.PageSize)).MustHaveHappenedOnceExactly();
        A.CallTo(() => serviceClient.ListBlueprintsAsync(A<int>._, A<int>._)).MustHaveHappenedTwiceExactly();

        var result = ParseSingleOutput(lines);
        Assert.Equal(CatalogSize, result.Items.Count);
        Assert.Equal(CatalogSize, result.TotalCount);
        Assert.Equal(CatalogSize, result.Items.Select(i => i.Id).Distinct().Count());
        Assert.Contains(result.Items, i => i.Id == "Samples.Photovoltaics-1.1.1");
    }

    [Fact]
    public async Task Execute_WithSkipAndTake_FetchesExactlyThatPage()
    {
        var serviceClient = NewServiceClient(BuildCatalog(CatalogSize));
        var (console, lines) = NewRecordingConsole();
        var command = NewCommand(serviceClient, console);
        command.CommandArgumentValue.ParseLayer(["-s", "140", "-t", "5"]);

        await command.Execute();

        A.CallTo(() => serviceClient.ListBlueprintsAsync(140, 5)).MustHaveHappenedOnceExactly();
        A.CallTo(() => serviceClient.ListBlueprintsAsync(A<int>._, A<int>._)).MustHaveHappenedOnceExactly();
        var result = ParseSingleOutput(lines);
        Assert.Equal(5, result.Items.Count);
        Assert.Equal(CatalogSize, result.TotalCount);
    }

    [Fact]
    public async Task Execute_WithTakeOnly_FetchesFirstPageWithoutAutoPaging()
    {
        var serviceClient = NewServiceClient(BuildCatalog(CatalogSize));
        var (console, lines) = NewRecordingConsole();
        var command = NewCommand(serviceClient, console);
        command.CommandArgumentValue.ParseLayer(["--take", "10"]);

        await command.Execute();

        A.CallTo(() => serviceClient.ListBlueprintsAsync(0, 10)).MustHaveHappenedOnceExactly();
        A.CallTo(() => serviceClient.ListBlueprintsAsync(A<int>._, A<int>._)).MustHaveHappenedOnceExactly();
        Assert.Equal(10, ParseSingleOutput(lines).Items.Count);
    }

    [Fact]
    public async Task Execute_WithName_FiltersAcrossAllPages()
    {
        var serviceClient = NewServiceClient(BuildCatalog(CatalogSize));
        var (console, lines) = NewRecordingConsole();
        var command = NewCommand(serviceClient, console);
        command.CommandArgumentValue.ParseLayer(["-n", "samples.photovoltaics"]);

        await command.Execute();

        var result = ParseSingleOutput(lines);
        Assert.Equal(["Samples.Photovoltaics-1.0.0", "Samples.Photovoltaics-1.1.1"],
            result.Items.Select(i => i.Id).OrderBy(i => i, StringComparer.Ordinal).ToArray());
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task Execute_StopsOnEmptyPage_EvenIfTotalCountIsHigher()
    {
        var serviceClient = A.Fake<IAssetServicesClient>();
        A.CallTo(() => serviceClient.ListBlueprintsAsync(0, A<int>._))
            .Returns(new BlueprintCatalogListResponseDto { Items = [Item("A", "1.0.0")], TotalCount = 500 });
        A.CallTo(() => serviceClient.ListBlueprintsAsync(1, A<int>._))
            .Returns(new BlueprintCatalogListResponseDto { Items = [], TotalCount = 500 });
        var (console, lines) = NewRecordingConsole();
        var command = NewCommand(serviceClient, console);
        command.CommandArgumentValue.ParseLayer([]);

        await command.Execute();

        A.CallTo(() => serviceClient.ListBlueprintsAsync(A<int>._, A<int>._)).MustHaveHappenedTwiceExactly();
        Assert.Single(ParseSingleOutput(lines).Items);
    }

    [Fact]
    public async Task Execute_PrintsNothing_WhenCatalogIsEmpty()
    {
        var serviceClient = NewServiceClient([]);
        var (console, lines) = NewRecordingConsole();
        var command = NewCommand(serviceClient, console);
        command.CommandArgumentValue.ParseLayer([]);

        await command.Execute();

        Assert.Empty(lines);
    }

    private static List<BlueprintCatalogItemDto> BuildCatalog(int size)
    {
        // Mirrors test-2: the private catalog's newer version sits beyond the first page.
        var items = new List<BlueprintCatalogItemDto> { Item("Samples.Photovoltaics", "1.0.0") };
        for (var i = 0; items.Count < size - 1; i++)
        {
            items.Add(Item($"Blueprint{i:D3}", "1.0.0"));
        }

        items.Add(Item("Samples.Photovoltaics", "1.1.1", "PrivateGitHubBlueprintCatalog"));
        return items.Take(size).ToList();
    }

    private static BlueprintCatalogItemDto Item(string name, string version,
        string catalogName = "PublicGitHubBlueprintCatalog") =>
        new() { Id = $"{name}-{version}", Name = name, Version = version, CatalogName = catalogName };

    private static IAssetServicesClient NewServiceClient(List<BlueprintCatalogItemDto> catalog)
    {
        var serviceClient = A.Fake<IAssetServicesClient>();
        A.CallTo(() => serviceClient.ListBlueprintsAsync(A<int>._, A<int>._))
            .ReturnsLazily((int skip, int take) => Task.FromResult(new BlueprintCatalogListResponseDto
            {
                Items = catalog.Skip(skip).Take(take).ToList(),
                TotalCount = catalog.Count,
                Skip = skip,
                Take = take
            }));
        return serviceClient;
    }

    private static BlueprintCatalogListResponseDto ParseSingleOutput(List<string> lines)
    {
        var line = Assert.Single(lines);
        return JsonConvert.DeserializeObject<BlueprintCatalogListResponseDto>(line)!;
    }

    private static ListBlueprints NewCommand(IAssetServicesClient serviceClient, IConsoleService console) =>
        new(NullLogger<ListBlueprints>.Instance, console,
            Options.Create(new OctoToolOptions { TenantId = "octosystem" }), serviceClient,
            A.Fake<IAuthenticationService>());

    private static (IConsoleService Console, List<string> Lines) NewRecordingConsole()
    {
        var lines = new List<string>();
        var console = A.Fake<IConsoleService>();
        A.CallTo(() => console.WriteLine(A<string>._))
            .Invokes((string line) => lines.Add(line));
        return (console, lines);
    }
}
