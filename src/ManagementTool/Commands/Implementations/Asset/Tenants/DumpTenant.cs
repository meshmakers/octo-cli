using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;

internal class DumpTenant : JobOctoCommand
{
    private readonly IArgument _tenantIdArg;
    private readonly IArgument _fileArg;
    private readonly IArgument _includeArchiveDataArg;

    public DumpTenant(ILogger<DumpTenant> logger, IOptions<OctoToolOptions> options,
        IBotServicesClient botServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.BotServicesGroup, "Dump", "Dumps a tenant to a file",
            options, botServicesClient,
            authenticationService)
    {
        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId", ["Id of tenant"],
            true, 1);
        _fileArg = CommandArgumentValue.AddArgument("f", "file", ["File of backup (*.tar.gz)"], true, 1);
        _includeArchiveDataArg = CommandArgumentValue.AddArgument("iad", "include-archive-data",
            ["Include CrateDB archive data in the dump. The produced backup is a larger '*.octobak.zip' artifact"],
            false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_tenantIdArg, "mytenant"),
                    new CodeSampleArgument(_fileArg, "./backup.tar.gz"),
                ],
                    description: "Basic usage"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_tenantIdArg, "mytenant"),
                    new CodeSampleArgument(_fileArg, "./backup.octobak.zip"),
                    new CodeSampleArgument(_includeArchiveDataArg),
                ],
                    description: "Include CrateDB archive data (produces a larger '*.octobak.zip' artifact)"),
            ],
            Notes:
            [
                "Without --include-archive-data the backup is a plain '*.tar.gz' (MongoDB data only).",
                "With --include-archive-data the produced artifact is a larger '*.octobak.zip' that also carries the CrateDB archive rows; pick a matching file name with -f.",
            ]
        );

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower();
        var filePath = CommandArgumentValue.GetArgumentScalarValue<string>(_fileArg);
        var includeArchiveData = CommandArgumentValue.IsArgumentUsed(_includeArchiveDataArg);

        Logger.LogInformation(
            "Create dump of tenant \'{TenantId}\' at \'{ServiceClientServiceUri}\' (include archive data: {IncludeArchiveData})",
            tenantId, ServiceClient.ServiceUri, includeArchiveData);

        if (string.IsNullOrEmpty(tenantId))
        {
            throw ToolException.NoTenantIdConfigured();
        }

        var response = await ServiceClient.StartDumpRepositoryAsync(tenantId, includeArchiveData);
        Logger.LogInformation("Create dump with job id \'{Id}\' has been started", response.JobId);
        await WaitForJob(response.JobId);

        Logger.LogInformation(
            "Dump of tenant \'{TenantId}\' at \'{ServiceClientServiceUri}\' created. Downloading...", tenantId, ServiceClient.ServiceUri);
        await ServiceClient.DownloadDumpToFileAsync(tenantId, response.JobId, filePath,
            bytesDownloaded =>
            {
                Logger.LogInformation("Downloaded {Bytes:N0} bytes", bytesDownloaded);
            });
        Logger.LogInformation("Dump downloaded to \'{FilePath}\'", filePath);
    }
}