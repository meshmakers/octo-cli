using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Models;

internal class ExportRuntimeModelByDeepGraph : JobOctoCommand
{
    private readonly IAssetServicesClient _assetServicesClient;
    private readonly IArgument _fileArg;
    private readonly IArgument _originRtIdsArg;
    private readonly IArgument _originCkTypeIdArg;

    public ExportRuntimeModelByDeepGraph(ILogger<ExportRuntimeModelByDeepGraph> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IBotServicesClient botServiceClient,
        IAuthenticationService authenticationService)
        : base(logger, "ExportRtByDeepGraph",
            "Schedules a job to export runtime model graph by providing RtId's and type as starting point. File is specified using -f argument. The file is downloaded in ZIP-format after job is finished.",
            options, botServiceClient, authenticationService)
    {
        _assetServicesClient = assetServicesClient;

        _fileArg = CommandArgumentValue.AddArgument("f", "file", ["File to export"], true, 1);
        _originRtIdsArg =
            CommandArgumentValue.AddArgument("id", "runtime-identifiers", ["A semicolon separated list of RtIds to be used as starting point."], true, 1, true);
        _originCkTypeIdArg =
            CommandArgumentValue.AddArgument("t", "ckTypeId", ["The construction kit type id to be used as starting point."], true, 1);
    }

    public override async Task PreValidate()
    {
        await base.PreValidate();

        _assetServicesClient.AccessToken.AccessToken = ServiceClient.AccessToken.AccessToken;
    }

    public override async Task Execute()
    {
        var rtModelFilePath = CommandArgumentValue.GetArgumentScalarValue<string>(_fileArg);
        var originRtIdsArgumentValue = CommandArgumentValue.GetArgumentValue(_originRtIdsArg);
        var originCkTypeId = CommandArgumentValue.GetArgumentScalarValue<string>(_originCkTypeIdArg);
        var originRtIds = originRtIdsArgumentValue.Values.Select(OctoObjectId.Parse).ToList();

        var tenantId = Options.Value.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ServiceConfigurationMissingException("Tenant is missing.");
        }

        Logger.LogInformation("Exporting runtime data as deep graph to \'{RtModelFilePath}\'", rtModelFilePath);

        var id = await _assetServicesClient.ExportRtModelByDeepGraphAsync(tenantId, originRtIds, originCkTypeId);
        Logger.LogInformation("Runtime model export id \'{Id}\' has been started", id);
        await WaitForJob(id);

        await DownloadJobResultAsync(tenantId, id, rtModelFilePath);
    }
}
