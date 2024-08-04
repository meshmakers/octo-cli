using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Models;

internal class ExportRuntimeModelByQuery : JobOctoCommand
{
    private readonly IAssetServicesClient _assetServicesClient;
    private readonly IArgument _fileArg;
    private readonly IArgument _queryIdArg;

    public ExportRuntimeModelByQuery(ILogger<ExportRuntimeModelByQuery> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IBotServicesClient botServiceClient,
        IAuthenticationService authenticationService)
        : base(logger, "ExportRtByQuery",
            "Schedules a job to export runtime models using a query. File is specified using -f argument. The file is downloaded in ZIP-format after job is finished.",
            options, botServiceClient, authenticationService)
    {
        _assetServicesClient = assetServicesClient;

        _fileArg = CommandArgumentValue.AddArgument("f", "file", ["File to export"], true, 1);
        _queryIdArg =
            CommandArgumentValue.AddArgument("q", "queryId", ["Query ID that is used for export"], true, 1);
    }

    public override async Task PreValidate()
    {
        await base.PreValidate();

        _assetServicesClient.AccessToken.AccessToken = ServiceClient.AccessToken.AccessToken;
    }

    public override async Task Execute()
    {
        var rtModelFilePath = CommandArgumentValue.GetArgumentScalarValue<string>(_fileArg);
        var queryId = CommandArgumentValue.GetArgumentScalarValue<OctoObjectId>(_queryIdArg);

        var tenantId = Options.Value.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ServiceConfigurationMissingException("Tenant is missing.");
        }

        Logger.LogInformation("Exporting runtime data of query \'{QueryId}\' to \'{RtModelFilePath}\'", queryId,
            rtModelFilePath);

        var id = await _assetServicesClient.ExportRtModelByQueryAsync(tenantId, queryId);
        Logger.LogInformation("Runtime model export id \'{Id}\' has been started", id);
        await WaitForJob(id);

        await DownloadJobResultAsync(id, rtModelFilePath);
    }
}
