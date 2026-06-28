using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

internal class AddClientToRole : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _clientIdArg;
    private readonly IArgument _roleArg;

    public AddClientToRole(ILogger<AddClientToRole> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "AddClientToRole", "Assigns a role to a client", options,
            identityServicesClient, authenticationService)
    {
        _clientIdArg = CommandArgumentValue.AddArgument("id", "clientId", ["ID of the client"], true, 1);
        _roleArg = CommandArgumentValue.AddArgument("r", "role", ["Existing role name"], true, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_clientIdArg, "ci-deploy"),
                    new CodeSampleArgument(_roleArg, "DataAnalyst"),
                ],
                    description: "Assign a role to a client"),
            ]
        );

    public override async Task Execute()
    {
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientIdArg);
        var roleName = CommandArgumentValue.GetArgumentScalarValue<string>(_roleArg);

        Logger.LogInformation("Assigning role '{Role}' to client '{ClientId}' at '{ServiceClientServiceUri}'",
            roleName, clientId, ServiceClient.ServiceUri);

        await ServiceClient.AddRoleToClient(clientId, roleName);

        Logger.LogInformation("Client '{ClientId}' updated", clientId);
    }
}
