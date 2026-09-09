using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;

internal class GetTenants : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _allArg;
    private readonly IArgument _recursiveArg;

    public GetTenants(ILogger<GetTenants> logger,
        IConsoleService consoleService,
        IOptions<OctoToolOptions> options, IAssetServicesClient assetServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "GetTenants",
            "Gets all direct child tenants, or with --all every tenant of the installation.", options,
            assetServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _allArg = CommandArgumentValue.AddArgument("a", "all",
        [
            "Return every tenant registered on the installation, including sub-tenants of nested parents " +
            "and re-parented tenants. Only answered on the system tenant."
        ], false, 0);
        _recursiveArg = CommandArgumentValue.AddArgument("r", "recursive",
            [
                "Also list every deeper descendant (grandchildren and below), each entry carrying its",
                "parentTenantId. Without it, only DIRECT child tenants are returned — sub-tenant",
                "hierarchies stay invisible (AB#5151). Requires a service exposing tenants/descendants;",
                "an older service fails the call instead of silently returning only the children."
            ], false);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [], description: "Direct child tenants of the current tenant"),
                new CodeSample(arguments: [new CodeSampleArgument(_allArg)],
                    description: "Every tenant of the installation (system tenant context only)"),
            ],
            Notes:
            [
                "Without --all only DIRECT children of the current tenant are returned (AB#5025). A tenant " +
                "that hangs below another tenant — nested from the start or re-parented later — does NOT " +
                "appear in the system tenant's plain list; enumerate the full installation with --all instead " +
                "(AB#5151, AB#5129).",
                "--all reads the platform-wide routing registry and is therefore only answered on the system " +
                "tenant; any other tenant receives HTTP 403.",
            ]
        );

    public override async Task Execute()
    {
        var all = CommandArgumentValue.IsArgumentUsed(_allArg);
        if (all)
        {
            Logger.LogInformation("Getting all tenants of the installation from '{ServiceClientServiceUri}'",
                ServiceClient.ServiceUri);
        }
        else
        {
            Logger.LogInformation("Getting tenants from '{ServiceClientServiceUri}'", ServiceClient.ServiceUri);
        }

        var result = all
            ? await ServiceClient.GetAllTenantsAsync()
            : CommandArgumentValue.IsArgumentUsed(_recursiveArg)
                ? await ServiceClient.GetTenantDescendantsAsync()
                : await ServiceClient.GetTenantsAsync();

        var tenants = result.ToArray();
        if (!tenants.Any())
        {
            Logger.LogInformation("No tenants have been returned");
            return;
        }

        var resultString = JsonConvert.SerializeObject(tenants, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }
}
