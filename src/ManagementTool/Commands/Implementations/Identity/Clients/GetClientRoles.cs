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
/// </summary>
internal class GetClientRoles : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _clientIdArg;

    public GetClientRoles(ILogger<GetClientRoles> logger, IOptions<OctoToolOptions> options,
        IConsoleService consoleService,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "GetClientRoles",
            "Lists the roles assigned directly to a client in the tenant (without roles inherited from groups).",
            options, identityServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _clientIdArg = CommandArgumentValue.AddArgument("id", "clientId",
            ["The ClientId whose direct roles you want to list"], true, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_clientIdArg, "ci-deploy")],
                    description: "List the roles assigned directly to the client 'ci-deploy'"),
            ],
            Notes:
            [
                "Prints a JSON array of the client's direct roles (Id and Name, sorted by name; [] when it has none). Roles the client gets through a group are not listed. No secret is printed. Standard output carries only the JSON (logs go to standard error), so it can be piped to jq.",
                "Reads the tenant of the context (switch it per call with --context). An unknown client or tenant ends with a message naming it and exit code -5; a context whose tenant does not exist usually stops earlier, at sign-in (the token for that tenant is refused, exit code -4).",
                "A role id that is no role of the tenant is printed with Name null and a warning.",
                "Needs a role that may read the identity directory in the tenant (e.g. TenantManagement or UserManagement)."
            ]
        );

    public override async Task Execute()
    {
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientIdArg);
        var tenantId = Options.Value.TenantId;

        Logger.LogInformation("Getting the direct roles of client '{ClientId}' from '{ServiceClientServiceUri}'",
            clientId, ServiceClient.ServiceUri);

        IEnumerable<RoleDto> tenantRoles;
        try
        {
            tenantRoles = await ServiceClient.GetRoles();
        }
        catch (ServiceClientResultException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            throw new ToolException(
                $"Tenant '{tenantId}' was not found at '{ServiceClient.ServiceUri.GetLeftPart(UriPartial.Authority)}'.");
        }

        IEnumerable<string> roleIds;
        try
        {
            roleIds = await ServiceClient.GetClientDirectRoles(clientId);
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
