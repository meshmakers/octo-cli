using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.CrossTenantGrantPermissions;

/// <summary>
///     AB#8346: shows the allow-list of the context's tenant as a source of sibling-tenant grants (identity AB#8345).
/// </summary>
internal class GetCrossTenantGrantPermissions : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConsoleService _consoleService;

    public GetCrossTenantGrantPermissions(ILogger<GetCrossTenantGrantPermissions> logger,
        IOptions<OctoToolOptions> options, IConsoleService consoleService,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "GetCrossTenantGrantPermissions",
            "Shows which tenants may look up users of the tenant of the context by exact name or e-mail and grant them access.",
            options, identityServicesClient, authenticationService)
    {
        _consoleService = consoleService;
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples: [new CodeSample(arguments: [], description: "Show the allow-list of the tenant of the context")],
            Notes:
            [
                "An empty list means no tenant may (the default of every tenant). Registry ancestors need no entry.",
                "Only administrators of the tenant itself (UserManagement and TenantManagement, signed in to or switched into that tenant) may read the list; anyone else gets HTTP 403."
            ]);

    public override async Task Execute()
    {
        Logger.LogInformation("Getting cross-tenant grant permissions of tenant '{TenantId}' from '{ServiceClientServiceUri}'",
            Options.Value.TenantId, ServiceClient.ServiceUri);

        var permissions = await ServiceClient.GetCrossTenantGrantPermissions();
        if (permissions.TargetTenantIds.Count == 0)
        {
            Logger.LogInformation("No tenant may look up and grant users of this tenant");
        }

        _consoleService.WriteLine(JsonConvert.SerializeObject(permissions, Formatting.Indented));
    }
}
