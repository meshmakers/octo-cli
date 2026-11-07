using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.CrossTenantGrantPermissions;

/// <summary>
///     AB#8346: replaces the allow-list of the context's tenant as a source of sibling-tenant grants (identity AB#8345).
///     Reads the current list first, so the log names the tenants added and removed.
/// </summary>
internal class SetCrossTenantGrantPermissions : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _none;
    private readonly IArgument _targetTenantIds;

    public SetCrossTenantGrantPermissions(ILogger<SetCrossTenantGrantPermissions> logger,
        IOptions<OctoToolOptions> options, IConsoleService consoleService,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "SetCrossTenantGrantPermissions",
            "Replaces the list of tenants that may look up users of the tenant of the context by exact name or e-mail and grant them access.",
            options, identityServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _targetTenantIds = CommandArgumentValue.AddArgument("ttids", "targetTenantIds",
            ["Comma-separated list of the permitted target tenant IDs (replaces the whole list)"], false, 1);
        _none = CommandArgumentValue.AddArgument("none", "none",
            ["Withdraw every permission (empty list)"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_targetTenantIds, "customer-a,customer-b")],
                    description: "Permit two tenants"),
                new CodeSample(arguments: [new CodeSampleArgument(_none)], description: "Permit nobody")
            ],
            Notes:
            [
                "Pass exactly one of --targetTenantIds or --none. The list replaces the stored one; entries are trimmed, lower-cased and de-duplicated by the service.",
                "A removed tenant can no longer look up or grant users at once; its existing grants are refused at their next token exchange.",
                "Invalid entries (format, more than 200, the tenant itself) are refused with HTTP 400 and nothing changes. Entries are not checked for existence.",
                "Only administrators of the tenant itself (UserManagement and TenantManagement, signed in to or switched into that tenant) may change the list; anyone else gets HTTP 403. Every change is written to the tenant's event log."
            ]);

    public override async Task Execute()
    {
        var hasList = CommandArgumentValue.IsArgumentUsed(_targetTenantIds);
        var none = CommandArgumentValue.IsArgumentUsed(_none);
        if (hasList == none)
        {
            throw new ToolException("Pass exactly one of --targetTenantIds or --none.");
        }

        var requested = hasList
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_targetTenantIds)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            : [];
        if (hasList && requested.Count == 0)
        {
            throw new ToolException("--targetTenantIds is empty; use --none to permit nobody.");
        }

        var tenantId = Options.Value.TenantId;
        Logger.LogInformation("Setting cross-tenant grant permissions of tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            tenantId, ServiceClient.ServiceUri);

        var before = await ServiceClient.GetCrossTenantGrantPermissions();
        var stored = await ServiceClient.UpdateCrossTenantGrantPermissions(
            new CrossTenantGrantPermissionsDto { TargetTenantIds = requested });

        var added = stored.TargetTenantIds.Except(before.TargetTenantIds, StringComparer.OrdinalIgnoreCase).ToList();
        var removed = before.TargetTenantIds.Except(stored.TargetTenantIds, StringComparer.OrdinalIgnoreCase).ToList();
        Logger.LogInformation("Cross-tenant grant permissions of tenant '{TenantId}' stored: added [{Added}], removed [{Removed}]",
            tenantId, string.Join(", ", added), string.Join(", ", removed));

        _consoleService.WriteLine(JsonConvert.SerializeObject(stored, Formatting.Indented));
    }
}
