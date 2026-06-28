using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

internal class RemoveClientFromRole : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _clientIdArg;
    private readonly IArgument _roleArg;
    private readonly IArgument _yesArg;

    public RemoveClientFromRole(ILogger<RemoveClientFromRole> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.IdentityServicesGroup, "RemoveClientFromRole", "Removes a role from a client", options,
            identityServicesClient, authenticationService)
    {
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

        await ServiceClient.RemoveRoleFromClient(clientId, roleName);

        Logger.LogInformation("Client '{ClientId}' updated", clientId);
    }
}
