using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Roles;

internal class CreateRole : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IdentityMethodCall _methods;
    private readonly IArgument _nameArg;

    public CreateRole(ILogger<CreateRole> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "CreateRole", "Create a new role", options,
            identityServicesClient,
            authenticationService)
    {
        _methods = new IdentityMethodCall(tenantClient, authenticationService, logger);
        _nameArg = CommandArgumentValue.AddArgument("n", "name", ["Name of role"], true,
            1);
    }


    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_nameArg, "DataAnalyst")], description: "Basic usage"),
            ]
        );

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg);

        Logger.LogInformation("Creating role \'{Name}\' at \'{ServiceClientServiceUri}\'", name,
            ServiceClient.ServiceUri);


        var roleDto = new RoleDto
        {
            Name = name
        };

        await _methods.RunAsync(RtRoleMethodClient.CreateRoleMethodId,
            () => new RtRoleMethodClient(_methods.TenantClient).CreateRoleAsync(name));

        Logger.LogInformation("Role \'{Name}\' added", name);
    }
}