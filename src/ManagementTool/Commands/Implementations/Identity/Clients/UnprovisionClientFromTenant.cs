using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

internal class UnprovisionClientFromTenant : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _clientId;
    private readonly IArgument _childTenantId;
    private readonly IArgument _yesArg;

    public UnprovisionClientFromTenant(ILogger<UnprovisionClientFromTenant> logger,
        IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.IdentityServicesGroup, "UnprovisionClientFromTenant",
            "Removes a single client mirror (drops the child-side client + the parent's tracking row).",
            options, identityServicesClient, authenticationService)
    {
        _confirmationService = confirmationService;

        _clientId = CommandArgumentValue.AddArgument("id", "clientId",
            ["The mirrored ClientId"], true, 1);
        _childTenantId = CommandArgumentValue.AddArgument("ctid", "child-tenant-id",
            ["ID of the child tenant whose mirror should be removed"], true, 1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override async Task Execute()
    {
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientId);
        var childTenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_childTenantId);

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm(
                $"Remove client '{clientId}' from child tenant '{childTenantId}'? The mirror client will be erased from the child tenant's identity DB."))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation(
            "Unprovisioning client '{ClientId}' from child tenant '{ChildTenantId}' at '{ServiceClientServiceUri}'",
            clientId, childTenantId, ServiceClient.ServiceUri);

        await ServiceClient.UnprovisionClientFromTenant(clientId, childTenantId);

        Logger.LogInformation("Mirror for client '{ClientId}' in child '{ChildTenantId}' removed",
            clientId, childTenantId);
    }
}
