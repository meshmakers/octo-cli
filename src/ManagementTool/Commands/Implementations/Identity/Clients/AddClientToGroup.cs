using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

internal class AddClientToGroup : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _id;
    private readonly IArgument _clientId;

    public AddClientToGroup(ILogger<AddClientToGroup> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "AddClientToGroup", "Adds a client to a group.", options,
            identityServicesClient, authenticationService)
    {
        _id = CommandArgumentValue.AddArgument("id", "identifier", ["ID of the group"], true, 1);
        _clientId = CommandArgumentValue.AddArgument("cid", "clientId", ["ID of the client to add"], true, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_id, "<group-rtid>"),
                    new CodeSampleArgument(_clientId, "ci-deploy"),
                ],
                    description: "Basic usage"),
            ]
        );

    public override async Task Execute()
    {
        var rtId = CommandArgumentValue.GetArgumentScalarValue<OctoObjectId>(_id);
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientId);

        Logger.LogInformation("Adding client '{ClientId}' to group '{RtId}' at '{ServiceClientServiceUri}'",
            clientId, rtId, ServiceClient.ServiceUri);

        await ServiceClient.AddClientToGroup(rtId, clientId);

        Logger.LogInformation("Client '{ClientId}' added to group '{RtId}'", clientId, rtId);
    }
}
