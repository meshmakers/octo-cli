using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

internal class RemoveClientFromRole : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IdentityMethodCall _methods;
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _clientIdArg;
    private readonly IArgument _roleArg;
    private readonly IArgument _yesArg;

    public RemoveClientFromRole(ILogger<RemoveClientFromRole> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.IdentityServicesGroup, "RemoveClientFromRole", "Removes a role from a client", options,
            identityServicesClient, authenticationService)
    {
        _methods = new IdentityMethodCall(tenantClient, authenticationService, logger);
        _confirmationService = confirmationService;

        _clientIdArg = CommandArgumentValue.AddArgument("id", "clientId", ["ID of the client"], true, 1);
        _roleArg = CommandArgumentValue.AddArgument("r", "role", ["Role name"], true, 1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_clientIdArg, "ci-deploy"),
                    new CodeSampleArgument(_roleArg, "DataAnalyst"),
                ],
                    description: "Remove a role from a client"),
            ]
        );

    public override async Task Execute()
    {
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientIdArg);
        var roleName = CommandArgumentValue.GetArgumentScalarValue<string>(_roleArg);

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm(
                $"Are you sure you want to remove client '{clientId}' from role '{roleName}'?"))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation("Removing role '{Role}' from client '{ClientId}' at '{ServiceClientServiceUri}'",
            roleName, clientId, ServiceClient.ServiceUri);

        await _methods.RunAsync(RtClientMethodClient.RemoveRoleMethodId,
            async () => await new RtClientMethodClient(_methods.TenantClient).RemoveRoleAsync(
                await IdentityMethodCall.ResolveClientRtIdAsync(ServiceClient, clientId), roleName),
            // As the REST endpoint: an unknown role is a success without a change.
            treatAsSuccess: e => e.Code == RtClientMethodClient.RemoveRoleErrors.RoleNotFound);

        Logger.LogInformation("Client '{ClientId}' updated", clientId);
    }
}
