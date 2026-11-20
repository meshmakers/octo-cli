using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.ApiResources;

internal class DeleteApiResource : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IdentityMethodCall _methods;
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _name;
    private readonly IArgument _yesArg;

    public DeleteApiResource(ILogger<DeleteApiResource> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.IdentityServicesGroup, "DeleteApiResource", "Deletes an api resource.", options,
            identityServicesClient, authenticationService)
    {
        _methods = new IdentityMethodCall(tenantClient, authenticationService, logger);
        _confirmationService = confirmationService;

        _name = CommandArgumentValue.AddArgument("n", "name", ["Name of resource"],
            true,
            1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_name, "myAPI")], description: "Basic usage"),
            ]
        );

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_name);

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm($"Are you sure you want to delete API resource '{name}'?"))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation("Deleting API resource \'{Name}\' from \'{ServiceClientServiceUri}\'", name,
            ServiceClient.ServiceUri);

        await _methods.RunAsync(RtApiResourceMethodClient.DeleteApiResourceMethodId,
            async () => await new RtApiResourceMethodClient(_methods.TenantClient).DeleteApiResourceAsync(
                await _methods.ResolveApiResourceRtIdAsync(name)));

        Logger.LogInformation("API resource \'{Name}\' deleted", name);
    }
}
