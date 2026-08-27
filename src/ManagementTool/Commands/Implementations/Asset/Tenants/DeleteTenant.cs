using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;

internal class DeleteTenant : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _tenantIdArg;
    private readonly IArgument _yesArg;

    public DeleteTenant(ILogger<DeleteTenant> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "Delete", "Deletes an existing tenant.", options,
            assetServicesClient, authenticationService)
    {
        _confirmationService = confirmationService;

        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId", ["Id of tenant"],
            true, 1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_tenantIdArg, "newtenant")], description: "Basic usage"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_tenantIdArg, "newtenant"),
                    new CodeSampleArgument(_yesArg),
                ],
                    description: "Non-interactive"),
            ],
            Notes:
            [
                "Refused with HTTP 409 while Stream Data, Communication, Reporting or AI Services is still enabled for the tenant; " +
                "the error names the enabled capabilities. Disable them first with DisableStreamData / DisableCommunication / " +
                "DisableReporting / DisableAi. Those commands act on the tenant of the active context, so switch to the tenant " +
                "being deleted with UseContext or pass --context <name>.",
                "If the tenant's data is still needed, take a backup with Dump before disabling the capabilities and deleting; " +
                "Dump works regardless of capability state.",
                "Answers 404 when the tenant is not a child of the current tenant.",
                "A 409 is also answered while the tenant is still being created; that message names the lifecycle state, " +
                "not capabilities - retry once the tenant is active or failed.",
            ]
        );

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower();

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm(
                $"Are you sure you want to delete tenant '{tenantId}' of {ParentScopeDescription}?"))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation("Deleting tenant \'{TenantId}\' on at \'{ServiceClientServiceUri}\'", tenantId,
            ServiceClient.ServiceUri);

        await ServiceClient.DeleteTenantAsync(tenantId);

        Logger.LogInformation("Tenant \'{TenantId}\' on at \'{ServiceClientServiceUri}\' deleted", tenantId,
            ServiceClient.ServiceUri);
    }
}
