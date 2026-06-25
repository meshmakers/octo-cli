using System.Globalization;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

internal class ExportArchiveData : JobOctoCommand
{
    private readonly IArgument _tenantIdArg;
    private readonly IArgument _archiveRtIdArg;
    private readonly IArgument _fromUtcArg;
    private readonly IArgument _toUtcArg;
    private readonly IArgument _outputArg;

    public ExportArchiveData(ILogger<ExportArchiveData> logger, IOptions<OctoToolOptions> options,
        IBotServicesClient botServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.BotServicesGroup, "ExportArchiveData",
            "Exports the row data of an archive to a downloadable ZIP. Omit both --fromUtc and --toUtc to export the whole archive; supply them to export the half-open slice [fromUtc, toUtc).",
            options, botServicesClient,
            authenticationService)
    {
        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId", ["Id of tenant"],
            true, 1);
        _archiveRtIdArg = CommandArgumentValue.AddArgument("aid", "archiveRtId",
            ["Runtime id of the CkArchive entity to export"], true, 1);
        _fromUtcArg = CommandArgumentValue.AddArgument("from", "fromUtc",
            ["Inclusive lower bound of the exported window, ISO-8601 UTC (e.g. 2026-05-11T14:00:00Z). Omit for whole archive."],
            false, 1);
        _toUtcArg = CommandArgumentValue.AddArgument("to", "toUtc",
            ["Exclusive upper bound of the exported window, ISO-8601 UTC (e.g. 2026-05-12T14:00:00Z). Omit for whole archive."],
            false, 1);
        _outputArg = CommandArgumentValue.AddArgument("o", "output", ["Destination path of the export (*.zip)"], true, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_tenantIdArg, "mytenant"),
                    new CodeSampleArgument(_archiveRtIdArg, "69fda707d47638c68edc7fea"),
                    new CodeSampleArgument(_outputArg, "./archive-export.zip"),
                ],
                    description: "Export the whole archive"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_tenantIdArg, "mytenant"),
                    new CodeSampleArgument(_archiveRtIdArg, "69fda707d47638c68edc7fea"),
                    new CodeSampleArgument(_fromUtcArg, "2026-05-11T00:00:00Z"),
                    new CodeSampleArgument(_toUtcArg, "2026-05-12T00:00:00Z"),
                    new CodeSampleArgument(_outputArg, "./archive-slice.zip"),
                ],
                    description: "Export a time slice [fromUtc, toUtc)"),
            ]
        );

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower();
        var archiveRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_archiveRtIdArg);
        var filePath = CommandArgumentValue.GetArgumentScalarValue<string>(_outputArg);

        if (string.IsNullOrEmpty(tenantId))
        {
            throw ToolException.NoTenantIdConfigured();
        }

        DateTime? fromUtc = null;
        if (CommandArgumentValue.IsArgumentUsed(_fromUtcArg))
        {
            var fromRaw = CommandArgumentValue.GetArgumentScalarValue<string>(_fromUtcArg);
            if (!DateTime.TryParse(fromRaw, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedFrom))
            {
                Logger.LogError("Could not parse 'fromUtc' value '{Raw}' as ISO-8601 timestamp.", fromRaw);
                return;
            }

            fromUtc = parsedFrom;
        }

        DateTime? toUtc = null;
        if (CommandArgumentValue.IsArgumentUsed(_toUtcArg))
        {
            var toRaw = CommandArgumentValue.GetArgumentScalarValue<string>(_toUtcArg);
            if (!DateTime.TryParse(toRaw, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedTo))
            {
                Logger.LogError("Could not parse 'toUtc' value '{Raw}' as ISO-8601 timestamp.", toRaw);
                return;
            }

            toUtc = parsedTo;
        }

        Logger.LogInformation(
            "Exporting archive '{ArchiveRtId}' of tenant '{TenantId}' at '{ServiceClientServiceUri}' (from: {FromUtc}, to: {ToUtc})",
            archiveRtId, tenantId, ServiceClient.ServiceUri, fromUtc?.ToString("O") ?? "<begin>",
            toUtc?.ToString("O") ?? "<end>");

        var response = await ServiceClient.StartExportArchiveDataAsync(tenantId, archiveRtId, fromUtc, toUtc);
        Logger.LogInformation("Export with job id '{Id}' has been started", response.JobId);
        await WaitForJob(response.JobId);

        Logger.LogInformation(
            "Export of archive '{ArchiveRtId}' of tenant '{TenantId}' at '{ServiceClientServiceUri}' created. Downloading...",
            archiveRtId, tenantId, ServiceClient.ServiceUri);
        await ServiceClient.DownloadDumpToFileAsync(tenantId, response.JobId, filePath,
            bytesDownloaded =>
            {
                Logger.LogInformation("Downloaded {Bytes:N0} bytes", bytesDownloaded);
            });
        Logger.LogInformation("Archive export downloaded to '{FilePath}'", filePath);
    }
}
