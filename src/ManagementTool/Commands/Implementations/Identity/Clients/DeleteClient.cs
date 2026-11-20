using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

internal class DeleteClient : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IdentityMethodCall _methods;
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _clientId;
    private readonly IArgument _yesArg;

    public DeleteClient(ILogger<DeleteClient> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.IdentityServicesGroup, "DeleteClient", "Deletes a client.", options,
            identityServicesClient, authenticationService)
    {
        _methods = new IdentityMethodCall(tenantClient, authenticationService, logger);
        _confirmationService = confirmationService;

        _clientId = CommandArgumentValue.AddArgument("id", "clientId", ["ServiceClient ID, must be unique"],
            true,
            1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_clientId, "my-web-app")], description: "Basic usage"),
            ]
        );

    public override async Task Execute()
    {
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientId);

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm($"Are you sure you want to delete client '{clientId}'?"))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation("Deleting client \'{ClientId}\' from \'{ServiceClientServiceUri}\'", clientId,
            ServiceClient.ServiceUri);

        await _methods.RunAsync(RtClientMethodClient.DeleteClientMethodId,
            async () => await new RtClientMethodClient(_methods.TenantClient).DeleteClientAsync(
                await IdentityMethodCall.ResolveClientRtIdAsync(ServiceClient, clientId)));

        Logger.LogInformation("ServiceClient \'{ClientId}\' deleted", clientId);
    }
}
