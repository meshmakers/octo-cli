using System.Net;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

/// <summary>
///     AB#9699: lists the roles assigned directly to a client in the context's tenant (no group-inherited roles), with
///     role id and name, so an operator can check a grant such as <c>CommunicationManagement</c> before and after a
///     change. Read-only; the output holds role ids and names only, never a client secret.
///     <para>
///         The roles of the tenant are read first: a tenant the identity service does not know answers 404 there,
///         which tells it apart from the 404 of an unknown client on the client's role endpoint.
///     </para>
///     <para>
///         AB#9872: with <c>--child-tenant-id</c> it reads a child tenant of the context's tenant with the caller's token
///         for that child (<see cref="IChildTenantIdentityFactory" />); the output is the same.
///     </para>
/// </summary>
internal class GetClientRoles : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IChildTenantIdentityFactory? _childTenantIdentityFactory;
    private readonly IArgument _clientIdArg;
    private readonly IArgument _childTenantIdArg;

    public GetClientRoles(ILogger<GetClientRoles> logger, IOptions<OctoToolOptions> options,
        IConsoleService consoleService,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService,
        IChildTenantIdentityFactory? childTenantIdentityFactory = null)
        : base(logger, Constants.IdentityServicesGroup, "GetClientRoles",
            "Lists the roles assigned directly to a client in the tenant (without roles inherited from groups).",
            options, identityServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _childTenantIdentityFactory = childTenantIdentityFactory;
        _clientIdArg = CommandArgumentValue.AddArgument("id", "clientId",
            ["The ClientId whose direct roles you want to list"], true, 1);
        _childTenantIdArg = CommandArgumentValue.AddArgument("ctid", "child-tenant-id",
            ["ID of a child tenant of the context's tenant to act in instead of the context's tenant; your token " +
             "is exchanged for that tenant (as Delete --disable-features does)"], false, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_clientIdArg, "ci-deploy")],
                    description: "List the roles assigned directly to the client 'ci-deploy'"),
                new CodeSample(arguments:
                    [
                        new CodeSampleArgument(_clientIdArg, "ci-deploy-test-2"),
                        new CodeSampleArgument(_childTenantIdArg, "customer-a"),
                    ],
                    description: "List the direct roles of the client in the child tenant 'customer-a' from the parent's context"),
            ],
            Notes:
            [
                "Prints a JSON array of the client's direct roles (Id and Name, sorted by name; [] when it has none). Roles the client gets through a group are not listed. No secret is printed. Standard output carries only the JSON (logs go to standard error), so it can be piped to jq.",
                "Reads the tenant of the context (switch it per call with --context). An unknown client or tenant ends with a message naming it and exit code -5; a context whose tenant does not exist usually stops earlier, at sign-in (the token for that tenant is refused, exit code -4).",
                "A role id that is no role of the tenant is printed with Name null and a warning.",
                "Needs a role that may read the identity directory in the tenant (e.g. TenantManagement or UserManagement).",
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
        var tenantId = Options.Value.TenantId;
        var identity = ServiceClient;

        if (CommandArgumentValue.IsArgumentUsed(_childTenantIdArg))
        {
            var child = await ChildTenantIdentityFactory.CreateForAsync(_childTenantIdentityFactory,
                CommandArgumentValue.GetArgumentScalarValue<string>(_childTenantIdArg));
            tenantId = child.TenantId;
            identity = child.Identity;
        }

        Logger.LogInformation("Getting the direct roles of client '{ClientId}' from '{ServiceClientServiceUri}'",
            clientId, identity.ServiceUri);

        IEnumerable<RoleDto> tenantRoles;
        try
        {
            tenantRoles = await identity.GetRoles();
        }
        catch (ServiceClientResultException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            throw new ToolException(
                $"Tenant '{tenantId}' was not found at '{identity.ServiceUri.GetLeftPart(UriPartial.Authority)}'.");
        }

        IEnumerable<string> roleIds;
        try
        {
            roleIds = await identity.GetClientDirectRoles(clientId);
        }
        catch (ServiceClientResultException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            throw new ToolException($"Client '{clientId}' was not found in tenant '{tenantId}'.");
        }

        var namesById = tenantRoles
            .Where(r => !string.IsNullOrEmpty(r.Id))
            .GroupBy(r => r.Id!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

        var result = roleIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => new RoleDto { Id = id, Name = namesById.GetValueOrDefault(id) })
            .OrderBy(r => r.Name ?? r.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var unnamed in result.Where(r => r.Name == null))
        {
            Logger.LogWarning("Role '{RoleId}' of client '{ClientId}' is not a role of tenant '{TenantId}'",
                unnamed.Id, clientId, tenantId);
        }

        if (result.Count == 0)
        {
            Logger.LogInformation("Client '{ClientId}' has no direct roles", clientId);
        }

        _consoleService.WriteLine(JsonConvert.SerializeObject(result, Formatting.Indented));
    }
}
