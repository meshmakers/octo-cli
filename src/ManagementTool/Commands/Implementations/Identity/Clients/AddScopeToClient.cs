using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

public class AddScopeToClient : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IdentityMethodCall _methods;
    private readonly IArgument _clientId;
    private readonly IArgument _scopeName;

    public AddScopeToClient(ILogger<AddScopeToClient> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "AddScopeToClient",
            "Grants the access to a client for a scope .", options,
            identityServicesClient, authenticationService)
    {
        _methods = new IdentityMethodCall(tenantClient, authenticationService, logger);
        _clientId = CommandArgumentValue.AddArgument("id", "clientId", ["ServiceClient ID, must be unique"],
            true,
            1);
        _scopeName = CommandArgumentValue.AddArgument("n", "name", ["Scope name"],
            true,
            1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_clientId, "my-web-app"),
                    new CodeSampleArgument(_scopeName, "assetSystemAPI.full_access"),
                ],
                    description: "Basic usage"),
            ]
        );

    public override async Task Execute()
    {
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientId);
        var scopeName = CommandArgumentValue.GetArgumentScalarValue<string>(_scopeName);

        Logger.LogInformation(
            "Allowing scope \'{ScopeName}\' for client \'{ClientId}\' at \'{ServiceClientServiceUri}\'",
            scopeName, clientId, ServiceClient.ServiceUri);

        var client = await ServiceClient.GetClient(clientId);

        var clientDto = new ClientDto
        {
            AllowedScopes = (client.AllowedScopes ?? new List<string>()).Append(scopeName)
        };

        await _methods.RunAsync(RtClientMethodClient.UpdateClientMethodId,
            () => ClientMethods.UpdateAsync(_methods.TenantClient,
                string.IsNullOrWhiteSpace(client.RtId) ? throw new RuntimeIdUnavailableException() : client.RtId,
                clientDto));

        Logger.LogInformation(
            "ServiceClient \'{ClientId}\' at \'{ServiceClientServiceUri}\' updated", clientId,
            ServiceClient.ServiceUri);
    }
}