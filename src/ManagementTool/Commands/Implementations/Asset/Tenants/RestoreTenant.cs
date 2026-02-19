using Meshmakers.Common.CommandLineParser;
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

    public RestoreTenant(ILogger<RestoreTenant> logger, IOptions<OctoToolOptions> options,
        IBotServicesClient botServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.BotServicesGroup, "Restore", "Restore a tenant from a dump file",
            options, botServicesClient,
            authenticationService)
    {
        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId", ["Id of tenant"],
            true, 1);
        _databaseArg = CommandArgumentValue.AddArgument("db", "database", ["Name of database"], true,
            1);
        _fileArg = CommandArgumentValue.AddArgument("f", "file", ["File of backup (*.tar.gz)"], true, 1);
        _oldDatabaseNameArg = CommandArgumentValue.AddArgument("oldDb", "oldDatabaseName", ["Name of the old database (if different to new database name)"], false, 1);
    }

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

        Logger.LogInformation(
            "Restoring tenant \'{TenantId}\' (database \'{DatabaseName}\') at \'{ServiceClientServiceUri}\'. Old database name: \'{oldDatabaseName}\'", tenantId,
            databaseName, ServiceClient.ServiceUri, oldDatabaseName ?? databaseName);

        if (string.IsNullOrEmpty(tenantId))
        {
            throw ToolException.NoTenantIdConfigured();
        }

        if (!File.Exists(filePath))
        {
            throw ToolException.FilePathDoesNotExist(filePath);
        }
        
        var response = await ServiceClient.RestoreRepositoryWithTusAsync(tenantId, databaseName, filePath, oldDatabaseName,
            progress =>
            {
                Logger.LogInformation("Upload progress: {Progress:P0}", progress);
            });
        Logger.LogInformation("Upload complete. Restore job with id \'{Id}\' has been started", response.JobId);
        await WaitForJob(response.JobId);
        Logger.LogInformation(
            "Tenant \'{TenantId}\' (database \'{DatabaseName}\') at \'{ServiceClientServiceUri}\' restored", tenantId,
            databaseName, ServiceClient.ServiceUri);
    }
}