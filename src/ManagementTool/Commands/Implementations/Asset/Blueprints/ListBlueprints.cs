using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Blueprints;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Blueprints;

/// <summary>
///     Lists the blueprint catalog. Without --skip/--take every page is fetched until the
///     service-reported total is reached (AB#5650: a single default page of 100 silently hid
///     every entry beyond it, e.g. newer versions served by a lower-priority catalog).
/// </summary>
internal class ListBlueprints : ServiceClientOctoCommand<IAssetServicesClient>
{
    /// <summary>
    ///     Page size used when fetching the full catalog.
    /// </summary>
    internal const int PageSize = 100;

    private readonly IConsoleService _consoleService;
    private readonly IArgument _skipArg;
    private readonly IArgument _takeArg;
    private readonly IArgument _nameArg;

    public ListBlueprints(
        ILogger<ListBlueprints> logger,
        IConsoleService consoleService,
        IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "ListBlueprints",
            "Lists blueprints available across configured catalogs. Fetches all pages unless -s/-t is given.",
            options, assetServicesClient, authenticationService)
    {
        _consoleService = consoleService;

        _skipArg = CommandArgumentValue.AddArgument("s", "skip",
            ["Number of catalog entries to skip (optional; fetches a single page when -s or -t is given)"],
            false, 1);
        _takeArg = CommandArgumentValue.AddArgument("t", "take",
            [$"Number of catalog entries to take (optional, default {PageSize}; fetches a single page when -s or -t is given)"],
            false, 1);
        _nameArg = CommandArgumentValue.AddArgument("n", "name",
            ["Blueprint name to filter by, case-insensitive exact match without version, e.g. 'Samples.Photovoltaics' (optional)"],
            false, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [], description: "List all blueprint versions of all catalogs"),
                new CodeSample(
                    arguments: [new CodeSampleArgument(_nameArg, "Samples.Photovoltaics")],
                    description: "List all versions of a single blueprint"),
                new CodeSample(
                    arguments: [new CodeSampleArgument(_skipArg, "100"), new CodeSampleArgument(_takeArg, "50")],
                    description: "Fetch a single page of the catalog"),
            ],
            Notes:
            [
                "Without -s/-t all pages are fetched; Items contains every entry and TotalCount the number of entries returned.",
                "With -s/-t exactly one page is fetched; TotalCount is the total reported by the service.",
                "The name filter (-n) is applied client-side to the fetched entries.",
            ]);

    public override async Task Execute()
    {
        Logger.LogInformation("Listing blueprints from '{ServiceClientServiceUri}'", ServiceClient.ServiceUri);

        var isSinglePage = CommandArgumentValue.IsArgumentUsed(_skipArg) ||
                           CommandArgumentValue.IsArgumentUsed(_takeArg);
        string? name = CommandArgumentValue.IsArgumentUsed(_nameArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg)
            : null;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = null;
        }

        BlueprintCatalogListResponseDto result;
        if (isSinglePage)
        {
            var skip = CommandArgumentValue.IsArgumentUsed(_skipArg)
                ? CommandArgumentValue.GetArgumentScalarValue<int>(_skipArg)
                : 0;
            var take = CommandArgumentValue.IsArgumentUsed(_takeArg)
                ? CommandArgumentValue.GetArgumentScalarValue<int>(_takeArg)
                : PageSize;
            if (skip < 0 || take <= 0)
            {
                Logger.LogError("Skip must be >= 0 and take must be > 0");
                return;
            }

            result = await ServiceClient.ListBlueprintsAsync(skip, take);
            if (name != null)
            {
                result.Items = FilterByName(result.Items, name);
            }
        }
        else
        {
            result = await ListAllAsync();
            if (name != null)
            {
                result.Items = FilterByName(result.Items, name);
                result.TotalCount = result.Items.Count;
                result.Take = result.Items.Count;
            }
        }

        if (result.Items.Count == 0)
        {
            if (name == null)
            {
                Logger.LogInformation("No blueprints found in any catalog");
            }
            else
            {
                Logger.LogInformation("No blueprints named '{Name}' found in any catalog", name);
            }

            return;
        }

        var resultString = JsonConvert.SerializeObject(result, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }

    private async Task<BlueprintCatalogListResponseDto> ListAllAsync()
    {
        var items = new List<BlueprintCatalogItemDto>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var skip = 0;
        var reportedTotal = 0;

        while (true)
        {
            var page = await ServiceClient.ListBlueprintsAsync(skip, PageSize);
            reportedTotal = page.TotalCount;

            // Dedupe by id: services before AB#5650 do not sort the merged catalog list, so an
            // entry can in theory move between pages if a catalog cache refreshes mid-listing.
            foreach (var item in page.Items)
            {
                if (seenIds.Add(item.Id))
                {
                    items.Add(item);
                }
            }

            skip += page.Items.Count;
            if (page.Items.Count == 0 || skip >= page.TotalCount)
            {
                break;
            }
        }

        if (items.Count != reportedTotal)
        {
            Logger.LogWarning(
                "Service reported {ReportedTotal} catalog entries but {ReceivedCount} distinct entries were received - the catalog may have changed while listing",
                reportedTotal, items.Count);
        }

        return new BlueprintCatalogListResponseDto
        {
            Items = items,
            TotalCount = items.Count,
            Skip = 0,
            Take = items.Count
        };
    }

    private static List<BlueprintCatalogItemDto> FilterByName(IEnumerable<BlueprintCatalogItemDto> items,
        string name) =>
        items.Where(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
}
