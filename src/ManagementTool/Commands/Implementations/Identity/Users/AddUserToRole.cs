using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Users;

/// <summary>
///     Adds a role to a user. Runs the CK method <c>System.Identity/User.AddRole-1</c> (AB#8417, see
///     <see cref="UserRoleMethodCall" />; servers without the method get the REST call, AB#8838); command name, arguments
///     and output are unchanged.
/// </summary>
internal class AddUserToRole : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _nameArg;
    private readonly IArgument _roleArg;
    private readonly UserRoleMethodCall _roleMethods;

    public AddUserToRole(ILogger<AddUserToRole> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "AddUserToRole", "Adds an user to a role", options,
            identityServicesClient, authenticationService)
    {
        _roleMethods = new UserRoleMethodCall(identityServicesClient, tenantClient, authenticationService, logger);
        _nameArg = CommandArgumentValue.AddArgument("un", "userName", ["User name"], true,
            1);
        _roleArg = CommandArgumentValue.AddArgument("r", "role", ["Existing role name"], false,
            1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_nameArg, "john.doe"),
                    new CodeSampleArgument(_roleArg, "DataAnalyst"),
                ],
                    description: "Basic usage"),
            ]
        );

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg).ToLower();
        var roleName = CommandArgumentValue.GetArgumentScalarValue<string>(_roleArg).ToLower();

        Logger.LogInformation("Adding user \'{Name}\' to role \'{Role}\' at \'{ServiceClientServiceUri}\'", name,
            roleName,
            ServiceClient.ServiceUri);

        if (!string.IsNullOrWhiteSpace(roleName))
        {
            try
            {
                await ServiceClient.GetRoleByName(roleName);
            }
            catch (ServiceClientResultException)
            {
                // AB#8838: a failure, so the Runner logs it and the command exits with -3 (it exited 0 before).
                throw new ServiceClientException(
                    $"Role '{roleName}' does not exist at Service '{ServiceClient.ServiceUri}'");
            }
        }

        await _roleMethods.AddRoleAsync(name, roleName);

        Logger.LogInformation("User \'{Name}\' updated", name);
    }
}