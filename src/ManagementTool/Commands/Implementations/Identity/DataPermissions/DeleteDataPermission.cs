using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.DataPermissions;

internal class DeleteDataPermission : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IdentityMethodCall _methods;
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _permissionIdArg;
    private readonly IArgument _yesArg;

    public DeleteDataPermission(ILogger<DeleteDataPermission> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.IdentityServicesGroup, "DeleteDataPermission",
            "Deletes a data permission including its policies", options, identityServicesClient,
            authenticationService)
    {
        _methods = new IdentityMethodCall(tenantClient, authenticationService, logger);
        _confirmationService = confirmationService;
        _permissionIdArg = CommandArgumentValue.AddArgument("pid", "permissionId",
            ["Dot-namespaced permission id"], true, 1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override async Task Execute()
    {
        var permissionId = CommandArgumentValue.GetArgumentScalarValue<string>(_permissionIdArg);

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm(
                $"Are you sure you want to delete data permission '{permissionId}' including its policies?"))
        {
            throw ToolException.OperationCancelledByUser();
        }

        await _methods.RunAsync(RtDataPermissionMethodClient.DeleteDataPermissionMethodId,
            async () => await new RtDataPermissionMethodClient(_methods.TenantClient).DeleteDataPermissionAsync(
                await IdentityMethodCall.ResolveDataPermissionRtIdAsync(ServiceClient, permissionId)));
        Logger.LogInformation("Data permission '{PermissionId}' deleted", permissionId);
    }
}
