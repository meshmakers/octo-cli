using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;

internal class CleanTenant : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _tenantIdArg;
    private readonly IArgument _yesArg;

    public CleanTenant(ILogger<CleanTenant> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IAuthenticationService authenticationServices,
        IConfirmationService confirmationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "Clean",
            "Resets a tenant to factory defaults by deleting the construction kit and runtime model.", options,
            assetServicesClient, authenticationServices)
    {
        _confirmationService = confirmationService;

        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId", ["Id of tenant"],
            true, 1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower();

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm(
                $"Are you sure you want to clean tenant '{tenantId}'? This will reset it to factory defaults"))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation("Cleaning tenant \'{TenantId}\' on at \'{ServiceClientServiceUri}\'", tenantId,
            ServiceClient.ServiceUri);

        await ServiceClient.CleanTenantAsync(tenantId);

        Logger.LogInformation("Tenant \'{TenantId}\' on at \'{ServiceClientServiceUri}\' cleaned", tenantId,
            ServiceClient.ServiceUri);
    }
}
