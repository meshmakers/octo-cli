using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Common.Shared;
using Meshmakers.Octo.Common.Shared.DataTransferObjects;
using Meshmakers.Octo.Frontend.Client.System;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations;

internal class ImportConstructionKitModel : JobWithWaitOctoCommand
{
    private readonly IAssetServicesClient _assetServicesClient;
    private readonly IArgument _fileArg;
    private readonly IArgument _scopeArg;

    public ImportConstructionKitModel(ILogger<ImportConstructionKitModel> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IBotServicesClient botServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, "ImportCk",
            "Schedules an import job for construction kit files. File is specified using -f argument. To wait for job, use -w argument.",
            options, botServicesClient, authenticationService)
    {
        _assetServicesClient = assetServicesClient;

        _fileArg = CommandArgumentValue.AddArgument("f", "file", new[] { "File to import" }, true, 1);
        _scopeArg = CommandArgumentValue.AddArgument("s", "scope",
            new[] { "Scope to import, available is 'Application', 'Layer2', 'Layer3' or 'Layer4'" }, true, 1);
    }

    public override async Task PreValidate()
    {
        await base.PreValidate();

        _assetServicesClient.AccessToken.AccessToken = ServicesClient.AccessToken.AccessToken;
    }

    public override async Task Execute()
    {
        var ckModelFilePath = CommandArgumentValue.GetArgumentScalarValue<string>(_fileArg).ToLower();
        var scopeId = CommandArgumentValue.GetArgumentScalarValue<ScopeIdsDto>(_scopeArg);

        var tenantId = Options.Value.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ServiceConfigurationMissingException("Tenant is missing.");
        }

        Logger.LogInformation("Importing construction kit model \'{CkModelFilePath}\'", ckModelFilePath);

        var id = await _assetServicesClient.ImportCkModel(tenantId, scopeId, ckModelFilePath);
        Logger.LogInformation("Construction kit model import id \'{Id}\' has been started", id);
        await WaitForJob(id);
    }
}
