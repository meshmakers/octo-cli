using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Models;

internal class ImportRuntimeModel : JobWithWaitOctoCommand
{
    private readonly IAssetServicesClient _assetServicesClient;
    private readonly IArgument _fileArg;
    private readonly IArgument _replaceArg;

    public ImportRuntimeModel(ILogger<ImportRuntimeModel> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IBotServicesClient botServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "ImportRt",
            "Schedules an import job for runtime files. File is specified using -f argument. To wait for job, use -w argument.",
            options, botServicesClient, authenticationService)
    {
        _assetServicesClient = assetServicesClient;

        _fileArg = CommandArgumentValue.AddArgument("f", "file", ["File to import"], true, 1);
        _replaceArg = CommandArgumentValue.AddArgument("r", "replace",
            ["When defined, existing entities are replaced."], false, 0);
    }

    public override async Task PreValidate()
    {
        await base.PreValidate();

        if (_assetServicesClient.AccessToken != null)
        {
            _assetServicesClient.AccessToken.AccessToken = ServiceClient.AccessToken?.AccessToken;
        }
    }

    public override async Task Execute()
    {
        var rtModelFilePath = CommandArgumentValue.GetArgumentScalarValue<string>(_fileArg);

        var importStrategy = CommandArgumentValue.IsArgumentUsed(_replaceArg)
            ? ImportStrategyDto.Upsert
            : ImportStrategyDto.InsertOnly;

        var tenantId = Options.Value.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw ToolException.NoTenantIdConfigured();
        }

        Logger.LogInformation("Importing runtime model \'{RtModelFilePath}\' with import mode \'{ImportStrategy}\'",
            rtModelFilePath, importStrategy);

        if (!File.Exists(rtModelFilePath))
        {
            Logger.LogError("File \'{CkModelFilePath}\' does not exist", rtModelFilePath);
            return;
        }

        var id = await _assetServicesClient.ImportRtModelAsync(tenantId, importStrategy, rtModelFilePath);
        Logger.LogInformation("Runtime model import with job id \'{Id}\' has been started", id);
        await WaitForJob(id);
    }
}