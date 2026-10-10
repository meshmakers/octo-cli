using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Files;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.PlatformFiles;

internal class GetFiles : ServiceClientOctoCommand<IAssetServicesClient>
{
    private const int PageSize = 200;

    private readonly IConsoleService _consoleService;
    private readonly IArgument _rootArg;
    private readonly IArgument _pathArg;
    private readonly IArgument _maxArg;

    public GetFiles(ILogger<GetFiles> logger, IConsoleService consoleService,
        IOptions<OctoToolOptions> options, IAssetServicesClient assetServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "GetFiles",
            "Lists the platform file system of the tenant: the folder roots, or the folders and files of a root or folder.",
            options, assetServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _rootArg = CommandArgumentValue.AddArgument("r", "root",
            ["Well-known name of the folder root, e.g. 'Files'. Without it the folder roots are listed."], false, 1);
        _pathArg = CommandArgumentValue.AddArgument("p", "path",
            ["Path of a folder below the root ('/'-separated). Without it the root itself is listed."], false, 1);
        _maxArg = CommandArgumentValue.AddArgument("n", "max",
            ["Maximum number of entries to return (default 1000)."], false, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [], description: "List the folder roots of the tenant"),
                new CodeSample(arguments: [new CodeSampleArgument(_rootArg, "Files")],
                    description: "List the content of the default root"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_rootArg, "Files"),
                    new CodeSampleArgument(_pathArg, "reports/2026"),
                ], description: "List the content of a folder"),
            ],
            Notes:
            [
                "Output is JSON: one entry per folder or file with rtId, kind ('root', 'folder' or 'file'), name, " +
                "size and content type. What you see is limited by your data permissions.",
                "Uses the file API of the asset repository service (GraphQL for metadata, " +
                "System.Files model, available in every tenant).",
            ]
        );

    public override async Task Execute()
    {
        var root = CommandArgumentValue.IsArgumentUsed(_rootArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_rootArg)
            : null;
        var path = CommandArgumentValue.IsArgumentUsed(_pathArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_pathArg).Trim('/')
            : null;
        var max = CommandArgumentValue.IsArgumentUsed(_maxArg)
            ? CommandArgumentValue.GetArgumentScalarValue<int>(_maxArg)
            : 1000;

        if (root == null && path != null)
        {
            throw new ToolException("The path argument (-p) requires a root (-r).");
        }

        if (max < 1)
        {
            throw new ToolException("The max argument (-n) must be at least 1.");
        }

        var files = ServiceClient.Files;
        var entries = new List<FileEntryDto>();
        string? cursor = null;
        while (entries.Count < max)
        {
            var first = Math.Min(PageSize, max - entries.Count);
            var page = root == null
                ? await files.ListRootsAsync(first, cursor)
                : await files.ListChildrenAsync(root, string.IsNullOrEmpty(path) ? null : path, first, cursor);
            entries.AddRange(page.Items);
            if (!page.HasNextPage || page.EndCursor == null || page.Items.Count == 0)
            {
                break;
            }

            cursor = page.EndCursor;
        }

        if (entries.Count == 0)
        {
            Logger.LogInformation("No entries have been returned");
            return;
        }

        _consoleService.WriteLine(JsonConvert.SerializeObject(entries, Formatting.Indented));
    }
}
