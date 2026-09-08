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
    private readonly IArgument _recursiveArg;

    public GetTenants(ILogger<GetTenants> logger,
        IConsoleService consoleService,
        IOptions<OctoToolOptions> options, IAssetServicesClient assetServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "GetTenants", "Gets all child tenants.", options,
            assetServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _recursiveArg = CommandArgumentValue.AddArgument("r", "recursive",
            [
                "Also list every deeper descendant (grandchildren and below), each entry carrying its",
                "parentTenantId. Without it, only DIRECT child tenants are returned — sub-tenant",
                "hierarchies stay invisible (AB#5151). Requires a service exposing tenants/descendants;",
                "an older service fails the call instead of silently returning only the children."
            ], false);
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Getting tenants from '{ServiceClientServiceUri}'", ServiceClient.ServiceUri);

        var result = CommandArgumentValue.IsArgumentUsed(_recursiveArg)
            ? await ServiceClient.GetTenantDescendantsAsync()
            : await ServiceClient.GetTenantsAsync();

        var tenants = result.ToArray();
        if (!tenants.Any())
        {
            Logger.LogInformation("No tenants have been returned");
            return;
        }

        var resultString = JsonConvert.SerializeObject(result, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }
}
