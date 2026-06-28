using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

internal class UpdateClientRoles : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _clientIdArg;
    private readonly IArgument _roleIdsArg;

    public UpdateClientRoles(ILogger<UpdateClientRoles> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "UpdateClientRoles",
            "Replaces the directly-assigned roles of a client (replace-all)", options,
            identityServicesClient, authenticationService)
    {
        _clientIdArg = CommandArgumentValue.AddArgument("id", "clientId", ["ID of the client"], true, 1);
        _roleIdsArg = CommandArgumentValue.AddArgument("rids", "roleIds",
            ["Comma-separated list of role IDs to assign (replace-all)"], true, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_clientIdArg, "ci-deploy"),
                    new CodeSampleArgument(_roleIdsArg, "660000000000000000000002,660000000000000000000009"),
                ],
                    description: "Set the directly-assigned roles of a client"),
            ]
        );

    public override async Task Execute()
    {
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientIdArg);
        var roleIdsRaw = CommandArgumentValue.GetArgumentScalarValue<string>(_roleIdsArg);
        var roleIds = roleIdsRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        Logger.LogInformation("Setting {RoleCount} role(s) on client '{ClientId}' at '{ServiceClientServiceUri}'",
            roleIds.Count, clientId, ServiceClient.ServiceUri);

        await ServiceClient.UpdateClientRoles(clientId, roleIds);

        Logger.LogInformation("Client '{ClientId}' roles updated", clientId);
    }
}
