using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations;

internal class ImportConstructionKitModel : JobWithWaitOctoCommand
{
    private readonly IAssetServicesClient _assetServicesClient;
    private readonly IArgument _fileArg;

    public ImportConstructionKitModel(ILogger<ImportConstructionKitModel> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IBotServicesClient botServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, "ImportCk",
            "Schedules an import job for construction kit files. File is specified using -f argument. To wait for job, use -w argument.",
            options, botServicesClient, authenticationService)
    {
        _assetServicesClient = assetServicesClient;

        _fileArg = CommandArgumentValue.AddArgument("f", "file", ["File to import"], true, 1);
    }

    public override async Task PreValidate()
    {
        await base.PreValidate();

        _assetServicesClient.AccessToken.AccessToken = ServiceClient.AccessToken.AccessToken;
    }

    public override async Task Execute()
    {
        var ckModelFilePath = CommandArgumentValue.GetArgumentScalarValue<string>(_fileArg).ToLower();

        var tenantId = Options.Value.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ServiceConfigurationMissingException("Tenant is missing.");
        }

        Logger.LogInformation("Importing construction kit model \'{CkModelFilePath}\'", ckModelFilePath);

        var id = await _assetServicesClient.ImportCkModelAsync(tenantId, ckModelFilePath);
        Logger.LogInformation("Construction kit model import id \'{Id}\' has been started", id);
        await WaitForJob(id);
    }
}