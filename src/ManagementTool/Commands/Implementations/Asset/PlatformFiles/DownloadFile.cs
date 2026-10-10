using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Files;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.PlatformFiles;

internal class DownloadFile : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IArgument _rootArg;
    private readonly IArgument _pathArg;
    private readonly IArgument _idArg;
    private readonly IArgument _fileArg;
    private readonly IArgument _overwriteArg;

    public DownloadFile(ILogger<DownloadFile> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "DownloadFile",
            "Downloads a file of the platform file system of the tenant to a local file.",
            options, assetServicesClient, authenticationService)
    {
        _rootArg = CommandArgumentValue.AddArgument("r", "root",
            ["Well-known name of the folder root (default 'Files'); used with -p."], false, 1);
        _pathArg = CommandArgumentValue.AddArgument("p", "path",
            ["Path of the file below the root ('/'-separated). Alternative to -id."], false, 1);
        _idArg = CommandArgumentValue.AddArgument("id", "rtId",
            ["Runtime id of the file. Alternative to -p."], false, 1);
        _fileArg = CommandArgumentValue.AddArgument("f", "file",
            ["Local target file. Default: the file name in the current directory."], false, 1);
        _overwriteArg = CommandArgumentValue.AddArgument("o", "overwrite",
            ["Overwrite the local file if it exists."], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_pathArg, "reports/2026/report.pdf"),
                    new CodeSampleArgument(_fileArg, "./report.pdf"),
                ], description: "Download a file of the default root by path"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_idArg, "68e296bc1f73f259bbb10399"),
                    new CodeSampleArgument(_fileArg, "./report.pdf"),
                    new CodeSampleArgument(_overwriteArg),
                ], description: "Download a file by id, replacing a local file of the same name"),
            ],
            Notes:
            ["The content is streamed to the local file; it is only moved into place when the transfer is complete."]
        );

    public override async Task Execute()
    {
        var hasPath = CommandArgumentValue.IsArgumentUsed(_pathArg);
        var hasId = CommandArgumentValue.IsArgumentUsed(_idArg);
        if (hasPath == hasId)
        {
            throw new ToolException("Specify either the path (-p) or the runtime id (-id) of the file.");
        }

        var root = CommandArgumentValue.IsArgumentUsed(_rootArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_rootArg)
            : FileSystemCkTypeIds.DefaultRootWellKnownName;
        var path = hasPath ? CommandArgumentValue.GetArgumentScalarValue<string>(_pathArg).Trim('/') : null;
        var overwrite = CommandArgumentValue.IsArgumentUsed(_overwriteArg);

        var files = ServiceClient.Files;
        await using var download = hasPath
            ? await files.DownloadAsync(root, path!)
            : await files.DownloadByIdAsync(CommandArgumentValue.GetArgumentScalarValue<string>(_idArg));

        var target = CommandArgumentValue.IsArgumentUsed(_fileArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_fileArg)
            : Path.GetFileName(download.FileName ?? path ?? "download.bin");
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new ToolException("Cannot derive a local file name; specify the target file (-f).");
        }

        if (File.Exists(target) && !overwrite)
        {
            throw new ToolException($"File '{target}' already exists. Use -o to overwrite it.");
        }

        var temp = $"{target}.{Guid.NewGuid():N}.part";
        try
        {
            await using (var output = File.Create(temp))
            {
                await download.Content.CopyToAsync(output);
            }

            File.Move(temp, target, overwrite);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }

        Logger.LogInformation("Downloaded to '{Target}' ({Size} bytes)", target, new FileInfo(target).Length);
    }
}
