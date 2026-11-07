using System.Net;
using System.Text.RegularExpressions;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.ExternalTenantUserMappings;

internal partial class CreateExternalTenantUserMapping : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _groups;
    private readonly IArgument _roleIds;
    private readonly IArgument _sourceTenantId;
    private readonly IArgument _sourceUserId;
    private readonly IArgument _sourceUserName;

    public CreateExternalTenantUserMapping(ILogger<CreateExternalTenantUserMapping> logger,
        IOptions<OctoToolOptions> options, IConsoleService consoleService,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "CreateExternalTenantUserMapping",
            "Creates an external tenant user mapping: grants a user of another tenant (registry ancestor or a sibling tenant that permits this one) access to the tenant of the context, with roles and/or groups.",
            options, identityServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _sourceTenantId = CommandArgumentValue.AddArgument("stid", "sourceTenantId",
            ["Source tenant ID"], true, 1);
        _sourceUserId = CommandArgumentValue.AddArgument("suid", "sourceUserId",
            ["Source user ID (userId of LookupSourceTenantUser)"], true, 1);
        _sourceUserName = CommandArgumentValue.AddArgument("sun", "sourceUserName",
            ["Source user name (userName of LookupSourceTenantUser; the service stores the name of the source user record)"],
            true, 1);
        _roleIds = CommandArgumentValue.AddArgument("rids", "roleIds",
            ["Comma-separated list of role IDs to assign"], false, 1);
        _groups = CommandArgumentValue.AddArgument("g", "groups",
            ["Comma-separated list of groups of the tenant of the context (group IDs or group names) the user becomes a member of"],
            false, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_sourceTenantId, "octosystem"),
                    new CodeSampleArgument(_sourceUserId, "<user-id-from-parent>"),
                    new CodeSampleArgument(_sourceUserName, "alice"),
                    new CodeSampleArgument(_roleIds, "Development,DashboardViewer"),
                ],
                    description: "Basic usage"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_sourceTenantId, "meshmakers"),
                    new CodeSampleArgument(_sourceUserId, "686a6215fd3e5972ff5693fd"),
                    new CodeSampleArgument(_sourceUserName, "alice"),
                    new CodeSampleArgument(_groups, "Operators,Viewers"),
                ],
                    description: "Grant a sibling-tenant user (found with LookupSourceTenantUser) with two groups"),
            ],
            Notes:
            [
                "Prints the mapping as stored: the user name of the source user record, the source tenant and the group names.",
                "A source tenant that is neither a registry ancestor nor permits the tenant of the context, or a user it does not have, is refused with HTTP 403, code SourceTenantNotPermitted (one answer for all of these).",
                "A group ID that is no group of this tenant is refused with HTTP 400, code UnknownGroup (nothing is stored); a second mapping for the same user with HTTP 409, code ExternalTenantUserMappingAlreadyExists.",
                "Groups need an identity service with AB#8343; older services ignore them."
            ]);

    public override async Task Execute()
    {
        var sourceTenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_sourceTenantId);

        Logger.LogInformation(
            "Creating external tenant user mapping for a user of tenant '{SourceTenantId}' at '{ServiceClientServiceUri}'",
            sourceTenantId, ServiceClient.ServiceUri);

        List<string>? roleIds = null;
        if (CommandArgumentValue.IsArgumentUsed(_roleIds))
        {
            roleIds = SplitList(CommandArgumentValue.GetArgumentScalarValue<string>(_roleIds));
        }

        List<string>? groupIds = null;
        if (CommandArgumentValue.IsArgumentUsed(_groups))
        {
            groupIds = await ResolveGroupIds(SplitList(CommandArgumentValue.GetArgumentScalarValue<string>(_groups)));
        }

        var dto = new CreateExternalTenantUserMappingDto
        {
            SourceTenantId = sourceTenantId,
            SourceUserId = CommandArgumentValue.GetArgumentScalarValue<string>(_sourceUserId),
            SourceUserName = CommandArgumentValue.GetArgumentScalarValue<string>(_sourceUserName),
            RoleIds = roleIds,
            GroupIds = groupIds
        };
        var mapping = await ServiceClient.GrantExternalTenantUser(dto);

        Logger.LogInformation("External tenant user mapping '{MappingId}' for '{SourceUserName}' of tenant '{SourceTenantId}' created",
            mapping.Id, mapping.SourceUserName, mapping.SourceTenantId);
        _consoleService.WriteLine(JsonConvert.SerializeObject(mapping, Formatting.Indented));
    }

    private static List<string> SplitList(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>
    ///     A 24-digit hex value is taken as group ID; anything else is a group name of the tenant of the context and is
    ///     resolved to its ID. An unknown name fails before the mapping is created.
    /// </summary>
    private async Task<List<string>> ResolveGroupIds(List<string> groups)
    {
        var ids = new List<string>(groups.Count);
        foreach (var group in groups)
        {
            if (ObjectIdPattern().IsMatch(group))
            {
                ids.Add(group);
                continue;
            }

            try
            {
                var dto = await ServiceClient.GetGroupByName(group);
                ids.Add(dto.Id?.ToString() ?? throw new ToolException($"Group '{group}' has no ID."));
            }
            catch (ServiceClientResultException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
            {
                throw new ToolException($"Group '{group}' does not exist in tenant '{Options.Value.TenantId}'.");
            }
        }

        return ids;
    }

    [GeneratedRegex("^[0-9a-fA-F]{24}$")]
    private static partial Regex ObjectIdPattern();
}
