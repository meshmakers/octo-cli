using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.ExternalTenantUserMappings;

internal class UpdateExternalTenantUserMapping : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IdentityMethodCall _methods;
    private readonly IArgument _id;
    private readonly IArgument _roleIds;

    public UpdateExternalTenantUserMapping(ILogger<UpdateExternalTenantUserMapping> logger,
        IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "UpdateExternalTenantUserMapping",
            "Updates an external tenant user mapping.", options, identityServicesClient, authenticationService)
    {
        _methods = new IdentityMethodCall(tenantClient, authenticationService, logger);
        _id = CommandArgumentValue.AddArgument("id", "identifier",
            ["ID of the external tenant user mapping"], true, 1);
        _roleIds = CommandArgumentValue.AddArgument("rids", "roleIds",
            ["Comma-separated list of role IDs to assign"], false, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_id, "<mapping-rtid>"),
                    new CodeSampleArgument(_roleIds, "Development,TenantManagement"),
                ],
                    description: "Basic usage"),
            ]
        );

    public override async Task Execute()
    {
        var rtId = OctoObjectId.Parse(CommandArgumentValue.GetArgumentScalarValue<string>(_id));

        Logger.LogInformation(
            "Updating external tenant user mapping '{RtId}' at '{ServiceClientServiceUri}'",
            rtId, ServiceClient.ServiceUri);

        List<string>? roleIds = null;
        if (CommandArgumentValue.IsArgumentUsed(_roleIds))
        {
            var roleIdsValue = CommandArgumentValue.GetArgumentScalarValue<string>(_roleIds);
            roleIds = roleIdsValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        var dto = new UpdateExternalTenantUserMappingDto
        {
            RoleIds = roleIds
        };
        await _methods.RunAsync(RtExternalTenantUserMappingMethodClient.UpdateMappingMethodId,
            () => new RtExternalTenantUserMappingMethodClient(_methods.TenantClient).UpdateMappingAsync(
                rtId.ToString(), dto.RoleIds));

        Logger.LogInformation("External tenant user mapping '{RtId}' updated", rtId);
    }
}
