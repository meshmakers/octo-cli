using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.ExternalTenantUserMappings;

internal class DeleteExternalTenantUserMapping : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IdentityMethodCall _methods;
    private readonly IArgument _id;

    public DeleteExternalTenantUserMapping(ILogger<DeleteExternalTenantUserMapping> logger,
        IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "DeleteExternalTenantUserMapping",
            "Deletes an external tenant user mapping.", options, identityServicesClient, authenticationService)
    {
        _methods = new IdentityMethodCall(tenantClient, authenticationService, logger);
        _id = CommandArgumentValue.AddArgument("id", "identifier",
            ["ID of the external tenant user mapping"], true, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_id, "<mapping-rtid>")], description: "Basic usage"),
            ]
        );

    public override async Task Execute()
    {
        var rtId = OctoObjectId.Parse(CommandArgumentValue.GetArgumentScalarValue<string>(_id));

        Logger.LogInformation(
            "Deleting external tenant user mapping '{RtId}' from '{ServiceClientServiceUri}'",
            rtId, ServiceClient.ServiceUri);

        await _methods.RunAsync(RtExternalTenantUserMappingMethodClient.DeleteMappingMethodId,
            () => new RtExternalTenantUserMappingMethodClient(_methods.TenantClient).DeleteMappingAsync(
                rtId.ToString()));

        Logger.LogInformation("External tenant user mapping '{RtId}' deleted", rtId);
    }
}
