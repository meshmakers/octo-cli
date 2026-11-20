using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

internal class AddClientToRole : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IdentityMethodCall _methods;
    private readonly IArgument _clientIdArg;
    private readonly IArgument _roleArg;

    public AddClientToRole(ILogger<AddClientToRole> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "AddClientToRole", "Assigns a role to a client", options,
            identityServicesClient, authenticationService)
    {
        _methods = new IdentityMethodCall(tenantClient, authenticationService, logger);
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

        await _methods.RunAsync(RtClientMethodClient.AddRoleMethodId,
            async () => await new RtClientMethodClient(_methods.TenantClient).AddRoleAsync(
                await IdentityMethodCall.ResolveClientRtIdAsync(ServiceClient, clientId), roleName));

        Logger.LogInformation("Client '{ClientId}' updated", clientId);
    }
}
