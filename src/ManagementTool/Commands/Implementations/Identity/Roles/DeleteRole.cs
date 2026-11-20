using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Roles;

internal class DeleteRole : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IdentityMethodCall _methods;
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _nameArg;
    private readonly IArgument _yesArg;

    public DeleteRole(ILogger<DeleteRole> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.IdentityServicesGroup, "DeleteRole", "Deletes a role", options, identityServicesClient,
            authenticationService)
    {
        _methods = new IdentityMethodCall(tenantClient, authenticationService, logger);
        _confirmationService = confirmationService;

        _nameArg = CommandArgumentValue.AddArgument("n", "name", ["Name of role"], true,
            1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_nameArg, "DataAnalyst")], description: "Basic usage"),
            ]
        );

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg).ToLower();

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm($"Are you sure you want to delete role '{name}'?"))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation("Deleting role \'{Name}\' at \'{ServiceClientServiceUri}\'", name,
            ServiceClient.ServiceUri);

        await _methods.RunAsync(RtRoleMethodClient.DeleteRoleMethodId,
            async () => await new RtRoleMethodClient(_methods.TenantClient).DeleteRoleAsync(
                await IdentityMethodCall.ResolveRoleRtIdAsync(ServiceClient, name)));

        Logger.LogInformation("Role \'{Name}\' deleted", name);
    }
}
