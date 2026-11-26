using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

/// <summary>
///     Removes a role from a client through the <c>System.Identity</c> method <c>RemoveRole</c>. AB#9872: with
///     <c>--child-tenant-id</c> in a child tenant of the context's tenant, with the caller's token for that child
///     (<see cref="IChildTenantIdentityFactory" />).
/// </summary>
internal class RemoveClientFromRole : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IdentityMethodCall _methods;
    private readonly IConfirmationService _confirmationService;
    private readonly IChildTenantIdentityFactory? _childTenantIdentityFactory;
    private readonly ILogger<RemoveClientFromRole> _logger;
    private readonly IArgument _clientIdArg;
    private readonly IArgument _roleArg;
    private readonly IArgument _yesArg;
    private readonly IArgument _childTenantIdArg;

    public RemoveClientFromRole(ILogger<RemoveClientFromRole> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService,
        IConfirmationService confirmationService,
        IChildTenantIdentityFactory? childTenantIdentityFactory = null)
        : base(logger, Constants.IdentityServicesGroup, "RemoveClientFromRole", "Removes a role from a client", options,
            identityServicesClient, authenticationService)
    {
        _logger = logger;
        _methods = new IdentityMethodCall(tenantClient, authenticationService, logger);
        _confirmationService = confirmationService;
        _childTenantIdentityFactory = childTenantIdentityFactory;

        _clientIdArg = CommandArgumentValue.AddArgument("id", "clientId", ["ID of the client"], true, 1);
        _roleArg = CommandArgumentValue.AddArgument("r", "role", ["Role name"], true, 1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
        _childTenantIdArg = CommandArgumentValue.AddArgument("ctid", "child-tenant-id",
            ["ID of a child tenant of the context's tenant to act in instead of the context's tenant; your token " +
             "is exchanged for that tenant (as Delete --disable-features does)"], false, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_clientIdArg, "ci-deploy"),
                    new CodeSampleArgument(_roleArg, "DataAnalyst"),
                ],
                    description: "Remove a role from a client"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_clientIdArg, "ci-deploy-test-2"),
                    new CodeSampleArgument(_roleArg, "CommunicationManagement"),
                    new CodeSampleArgument(_childTenantIdArg, "customer-a"),
                    new CodeSampleArgument(_yesArg),
                ],
                    description: "Remove a role from the client in the child tenant 'customer-a' from the parent's context"),
            ],
            Notes:
            [
                "--child-tenant-id <tenant> (AB#9872) acts in a child tenant (at any depth) of the context's tenant, e.g. from the " +
                "octosystem context in each customer tenant, without a context per tenant. The tenant must be listed by " +
                "GetTenants -r of the context, else the command stops with a message naming it (exit code -5). The command " +
                "uses YOUR token for that tenant, as Delete --disable-features: a signed-in user's token is exchanged for it " +
                "(the user must be provisioned there with a role, e.g. with ProvisionCurrentUser -ttid <tenant>), a " +
                "client-credentials session (OCTO_CLI_CLIENT_ID and a secret set) uses the client's mirror there " +
                "(OCTO_CLI_CHILD_CLIENT_SECRET when the mirror has its own secret). Without such a token the command stops " +
                "with the reason (exit code -5); a refusal in the child ends as in the context's tenant (exit code -3). " +
                "Options and output are the same as without the option."
            ]
        );

    public override async Task Execute()
    {
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientIdArg);
        var roleName = CommandArgumentValue.GetArgumentScalarValue<string>(_roleArg);
        var childTenantId = CommandArgumentValue.IsArgumentUsed(_childTenantIdArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_childTenantIdArg)
            : null;

        var question = childTenantId == null
            ? $"Are you sure you want to remove client '{clientId}' from role '{roleName}'?"
            : $"Are you sure you want to remove client '{clientId}' from role '{roleName}' in tenant '{childTenantId}'?";
        if (!CommandArgumentValue.IsArgumentUsed(_yesArg) && !_confirmationService.Confirm(question))
        {
            throw ToolException.OperationCancelledByUser();
        }

        var identity = ServiceClient;
        var methods = _methods;
        if (childTenantId != null)
        {
            var child = await ChildTenantIdentityFactory.CreateForAsync(_childTenantIdentityFactory, childTenantId);
            identity = child.Identity;
            methods = new IdentityMethodCall(child.Tenant, child.Authentication, _logger);
        }

        Logger.LogInformation("Removing role '{Role}' from client '{ClientId}' at '{ServiceClientServiceUri}'",
            roleName, clientId, identity.ServiceUri);

        await methods.RunAsync(RtClientMethodClient.RemoveRoleMethodId,
            async () => await new RtClientMethodClient(methods.TenantClient).RemoveRoleAsync(
                await IdentityMethodCall.ResolveClientRtIdAsync(identity, clientId), roleName),
            // As the REST endpoint: an unknown role is a success without a change.
            treatAsSuccess: e => e.Code == RtClientMethodClient.RemoveRoleErrors.RoleNotFound);

        Logger.LogInformation("Client '{ClientId}' updated", clientId);
    }
}
