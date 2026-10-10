using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Files;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.PlatformFiles;

internal class UploadFile : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IArgument _fileArg;
    private readonly IArgument _rootArg;
    private readonly IArgument _pathArg;
    private readonly IArgument _conflictArg;
    private readonly IArgument _createFoldersArg;
    private readonly IArgument _contentTypeArg;

    public UploadFile(ILogger<UploadFile> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "UploadFile",
            "Uploads a local file into the platform file system of the tenant.",
            options, assetServicesClient, authenticationService)
    {
        _fileArg = CommandArgumentValue.AddArgument("f", "file", ["Local file to upload"], true, 1);
        _rootArg = CommandArgumentValue.AddArgument("r", "root",
            ["Well-known name of the target folder root (default 'Files')."], false, 1);
        _pathArg = CommandArgumentValue.AddArgument("p", "path",
            ["Target path below the root, ending with the file name ('/'-separated). Default: the name of the local file in the root."],
            false, 1);
        _conflictArg = CommandArgumentValue.AddArgument("c", "conflict",
            ["What to do when the name exists: 'fail' (default), 'replace' or 'keepBoth'."], false, 1);
        _createFoldersArg = CommandArgumentValue.AddArgument("cf", "create-folders",
            ["Create missing folders on the way."], false, 0);
        _contentTypeArg = CommandArgumentValue.AddArgument("ct", "content-type",
            ["Content type; default: derived by the server from the file name."], false, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_fileArg, "./report.pdf")],
                    description: "Upload into the root of the default file root"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_fileArg, "./report.pdf"),
                    new CodeSampleArgument(_rootArg, "Files"),
                    new CodeSampleArgument(_pathArg, "reports/2026/report.pdf"),
                    new CodeSampleArgument(_createFoldersArg),
                    new CodeSampleArgument(_conflictArg, "replace"),
                ], description: "Upload into a folder path, creating missing folders and replacing an existing file"),
            ],
            Notes:
            [
                "conflict=replace stores new content for the existing file; its id and links to other entities stay. " +
                "keepBoth stores under the next free name ('a (1).pdf').",
                "The maximum file size is set by the server (default 100 MB per file).",
            ]
        );

    public override async Task Execute()
    {
        var file = CommandArgumentValue.GetArgumentScalarValue<string>(_fileArg);
        if (!File.Exists(file))
        {
            throw new ToolException($"File '{file}' does not exist.");
        }

        var root = CommandArgumentValue.IsArgumentUsed(_rootArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_rootArg)
            : FileSystemCkTypeIds.DefaultRootWellKnownName;
        var path = CommandArgumentValue.IsArgumentUsed(_pathArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_pathArg).Trim('/')
            : Path.GetFileName(file);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ToolException("The target path (-p) must end with a file name.");
        }

        var conflict = ParseConflict(CommandArgumentValue.IsArgumentUsed(_conflictArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_conflictArg)
            : "fail");
        var contentType = CommandArgumentValue.IsArgumentUsed(_contentTypeArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_contentTypeArg)
            : null;
        var createFolders = CommandArgumentValue.IsArgumentUsed(_createFoldersArg);

        Logger.LogInformation("Uploading '{File}' to '{Root}/{Path}'", file, root, path);

        await using var stream = File.OpenRead(file);
        var entry = await ServiceClient.Files.UploadAsync(root, path, stream, contentType, conflict, createFolders);

        Logger.LogInformation("{Action} '{Path}' (rtId {RtId}, {Size} bytes)",
            entry.Replaced ? "Replaced content of" : "Stored", entry.Path ?? path, entry.RtId, entry.Size);
    }

    internal static FileConflictMode ParseConflict(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "fail" => FileConflictMode.Fail,
            "replace" => FileConflictMode.Replace,
            "keepboth" => FileConflictMode.KeepBoth,
            _ => throw new ToolException($"Unknown conflict mode '{value}'. Use 'fail', 'replace' or 'keepBoth'.")
        };
}
