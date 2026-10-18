using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Commands;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

internal class ImportArchiveData : JobWithWaitOctoCommand
{
    private readonly IArgument _tenantIdArg;
    private readonly IArgument _archiveRtIdArg;
    private readonly IArgument _inputArg;
    private readonly IArgument _modeArg;
    private readonly IArgument _partSizeArg;
    private readonly IOptions<BotServiceClientOptions> _botOptions;

    public ImportArchiveData(ILogger<ImportArchiveData> logger, IOptions<OctoToolOptions> options,
        IBotServicesClient botServicesClient, IAuthenticationService authenticationService,
        IOptions<BotServiceClientOptions> botOptions)
        : base(logger, Constants.BotServicesGroup, "ImportArchiveData",
            "Imports archive row data from an export ZIP into a target archive. The target archive must be Disabled during the import (see DisableArchive). The bot validates that the export schema matches the target archive before any rows are written.",
            options, botServicesClient,
            authenticationService)
    {
        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId", ["Id of tenant"],
            true, 1);
        _archiveRtIdArg = CommandArgumentValue.AddArgument("aid", "archiveRtId",
            ["Runtime id of the target CkArchive entity"], true, 1);
        _inputArg = CommandArgumentValue.AddArgument("i", "input", ["Export file to import (*.zip)"], true, 1);
        _modeArg = CommandArgumentValue.AddArgument("m", "mode",
            ["Import mode: InsertOnly (default) or Upsert. Upsert is required for windowed (time-range / rollup) archives."],
            false, 1);
        _partSizeArg = CommandArgumentValue.AddArgument("ps", "part-size-mib",
            ["Size of one upload request in MiB (default 50, from 1 to 90). Keep it below the body limit of the ingress (100 MiB on staging-1, prod-1 and prod-2)."],
            false, 1);
        _botOptions = botOptions;
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_tenantIdArg, "mytenant"),
                    new CodeSampleArgument(_archiveRtIdArg, "69fda707d47638c68edc7fea"),
                    new CodeSampleArgument(_inputArg, "./archive-export.zip"),
                    new CodeSampleArgument(_waitForJobArg),
                ],
                    description: "Import in InsertOnly mode (default)"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_tenantIdArg, "mytenant"),
                    new CodeSampleArgument(_archiveRtIdArg, "69fda707d47638c68edc7fea"),
                    new CodeSampleArgument(_inputArg, "./archive-export.zip"),
                    new CodeSampleArgument(_modeArg, "Upsert"),
                    new CodeSampleArgument(_waitForJobArg),
                ],
                    description: "Import with upsert (required for windowed archives)"),
            ],
            Notes:
            [
                "The target archive must be Disabled during the import (use DisableArchive first, EnableArchive afterwards).",
                "The file is uploaded in parts of 50 MiB (change with --part-size-mib), so it passes the body limit of the ingress; an interrupted upload resumes from the server's offset.",
                "When the upload is refused, the command reports the HTTP status, the part number and the offset of the failed part, and no job is started.",
                "On failure the bot's error message (schema mismatch / archive-not-Disabled) is surfaced verbatim.",
            ]
        );

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower();
        var archiveRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_archiveRtIdArg);
        var filePath = CommandArgumentValue.GetArgumentScalarValue<string>(_inputArg);

        var mode = ArchiveImportMode.InsertOnly;
        if (CommandArgumentValue.IsArgumentUsed(_modeArg))
        {
            var modeRaw = CommandArgumentValue.GetArgumentScalarValue<string>(_modeArg);
            if (!Enum.TryParse(modeRaw, true, out mode))
            {
                Logger.LogError("Could not parse 'mode' value '{Raw}'. Valid values are: InsertOnly, Upsert.", modeRaw);
                return;
            }
        }

        if (string.IsNullOrEmpty(tenantId))
        {
            throw ToolException.NoTenantIdConfigured();
        }

        if (!File.Exists(filePath))
        {
            throw ToolException.FilePathDoesNotExist(filePath);
        }

        if (CommandArgumentValue.IsArgumentUsed(_partSizeArg))
        {
            TusUploadSupport.ApplyPartSize(_botOptions,
                CommandArgumentValue.GetArgumentScalarValue<int>(_partSizeArg));
        }

        Logger.LogInformation(
            "Importing archive data into archive '{ArchiveRtId}' of tenant '{TenantId}' at '{ServiceClientServiceUri}' (mode: {Mode})",
            archiveRtId, tenantId, ServiceClient.ServiceUri, mode);

        var progress = new UploadProgressLogger(Logger, new FileInfo(filePath).Length);
        var response = await ServiceClient.StartImportArchiveDataWithTusAsync(tenantId, archiveRtId, filePath, mode,
            progress.Report);
        Logger.LogInformation("Upload complete. Import job with id '{Id}' has been started", response.JobId);
        await WaitForJob(response.JobId);
        Logger.LogInformation(
            "Archive data imported into archive '{ArchiveRtId}' of tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            archiveRtId, tenantId, ServiceClient.ServiceUri);
    }
}
