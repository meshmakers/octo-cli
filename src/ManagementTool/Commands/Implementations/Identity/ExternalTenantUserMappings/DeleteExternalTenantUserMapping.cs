using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.ExternalTenantUserMappings;

internal class DeleteExternalTenantUserMapping : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _id;

    public DeleteExternalTenantUserMapping(ILogger<DeleteExternalTenantUserMapping> logger,
        IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "DeleteExternalTenantUserMapping",
            "Deletes an external tenant user mapping.", options, identityServicesClient, authenticationService)
    {
        _id = CommandArgumentValue.AddArgument("id", "identifier",
            ["ID of the external tenant user mapping"], true, 1);
    }

    public override async Task Execute()
    {
        var rtId = CommandArgumentValue.GetArgumentScalarValue<OctoObjectId>(_id);

        Logger.LogInformation(
            "Deleting external tenant user mapping '{RtId}' from '{ServiceClientServiceUri}'",
            rtId, ServiceClient.ServiceUri);

        await ServiceClient.DeleteExternalTenantUserMapping(rtId);

        Logger.LogInformation("External tenant user mapping '{RtId}' deleted", rtId);
    }
}
