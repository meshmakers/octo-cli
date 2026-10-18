using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Commands;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;

internal class RestoreTenant : JobWithWaitOctoCommand
{
    private readonly IArgument _databaseArg;
    private readonly IArgument _tenantIdArg;
    private readonly IArgument _fileArg;
    private readonly IArgument _oldDatabaseNameArg;
    private readonly IArgument _restoreArchiveDataArg;
    private readonly IArgument _partSizeArg;
    private readonly IOptions<BotServiceClientOptions> _botOptions;

    public RestoreTenant(ILogger<RestoreTenant> logger, IOptions<OctoToolOptions> options,
        IBotServicesClient botServicesClient, IAuthenticationService authenticationService,
        IOptions<BotServiceClientOptions> botOptions)
        : base(logger, Constants.BotServicesGroup, "Restore", "Restore a tenant from a dump file",
            options, botServicesClient,
            authenticationService)
    {
        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId", ["Id of tenant"],
            true, 1);
        _databaseArg = CommandArgumentValue.AddArgument("db", "database", ["Name of database"], true,
            1);
        _fileArg = CommandArgumentValue.AddArgument("f", "file", ["File of backup (*.tar.gz or *.octobak.zip)"], true, 1);
        _oldDatabaseNameArg = CommandArgumentValue.AddArgument("oldDb", "oldDatabaseName", ["Name of the old database (if different to new database name)"], false, 1);
        _restoreArchiveDataArg = CommandArgumentValue.AddArgument("rad", "restore-archive-data",
            ["Restore CrateDB archive data contained in an '*.octobak.zip' backup (no-op on a plain '*.tar.gz')"],
            false, 0);
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
                    new CodeSampleArgument(_databaseArg, "mytenant_db"),
                    new CodeSampleArgument(_fileArg, "./backup.tar.gz"),
                    new CodeSampleArgument(_waitForJobArg),
                ],
                    description: "Basic usage"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_tenantIdArg, "mytenant"),
                    new CodeSampleArgument(_databaseArg, "mytenant_db"),
                    new CodeSampleArgument(_fileArg, "./backup.octobak.zip"),
                    new CodeSampleArgument(_restoreArchiveDataArg),
                    new CodeSampleArgument(_waitForJobArg),
                ],
                    description: "Restore CrateDB archive data from an '*.octobak.zip' backup"),
            ],
            Notes:
            [
                "--restore-archive-data restores archives to their backed-up state; it is a no-op on a plain '*.tar.gz' backup.",
                "The backup is uploaded in parts of 50 MiB (change with --part-size-mib), so a backup of several GB passes the body limit of the ingress; an interrupted upload resumes from the server's offset.",
                "When the upload is refused, the command reports the HTTP status, the part number and the offset of the failed part, and no restore job is started.",
                "The target tenant's archive schemas must match those in the backup; mismatched archives are skipped and reported.",
            ]
        );

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower();
        var databaseName = CommandArgumentValue.GetArgumentScalarValue<string>(_databaseArg).ToLower();
        var filePath = CommandArgumentValue.GetArgumentScalarValue<string>(_fileArg);
        string? oldDatabaseName = null;
        if (CommandArgumentValue.IsArgumentUsed(_oldDatabaseNameArg))
        {
            oldDatabaseName = CommandArgumentValue.GetArgumentScalarValue<string>(_oldDatabaseNameArg);
        }

        var restoreArchiveData = CommandArgumentValue.IsArgumentUsed(_restoreArchiveDataArg);

        if (CommandArgumentValue.IsArgumentUsed(_partSizeArg))
        {
            TusUploadSupport.ApplyPartSize(_botOptions,
                CommandArgumentValue.GetArgumentScalarValue<int>(_partSizeArg));
        }

        Logger.LogInformation(
            "Restoring tenant \'{TenantId}\' (database \'{DatabaseName}\') at \'{ServiceClientServiceUri}\'. Old database name: \'{oldDatabaseName}\', restore archive data: {RestoreArchiveData}", tenantId,
            databaseName, ServiceClient.ServiceUri, oldDatabaseName ?? databaseName, restoreArchiveData);

        if (string.IsNullOrEmpty(tenantId))
        {
            throw ToolException.NoTenantIdConfigured();
        }

        if (!File.Exists(filePath))
        {
            throw ToolException.FilePathDoesNotExist(filePath);
        }

        var progress = new UploadProgressLogger(Logger, new FileInfo(filePath).Length);
        var response = await ServiceClient.RestoreRepositoryWithTusAsync(tenantId, databaseName, filePath, oldDatabaseName,
            restoreArchiveData: restoreArchiveData,
            progressCallback: progress.Report);
        Logger.LogInformation("Upload complete. Restore job with id \'{Id}\' has been started", response.JobId);
        await WaitForJob(response.JobId);
        Logger.LogInformation(
            "Tenant \'{TenantId}\' (database \'{DatabaseName}\') at \'{ServiceClientServiceUri}\' restored", tenantId,
            databaseName, ServiceClient.ServiceUri);
    }
}