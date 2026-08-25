using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;

internal class DetachTenant : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IArgument _tenantIdArg;

    public DetachTenant(ILogger<DetachTenant> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "Detach",
            "Detaches a child tenant. The database is kept and can be re-attached with Attach.", options,
            assetServicesClient, authenticationService)
    {
        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId", ["Id of tenant"],
            true, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_tenantIdArg, "newtenant")], description: "Basic usage"),
            ],
            Notes:
            [
                "Refused with HTTP 409 while Stream Data, Communication, Reporting or AI Services is still enabled for the tenant; " +
                "the error names the enabled capabilities. Disable them first with DisableStreamData / DisableCommunication / " +
                "DisableReporting / DisableAi. Those commands act on the tenant of the active context, so switch to the tenant " +
                "being detached with UseContext or pass --context <name>.",
                "If the tenant's data is still needed, take a backup with Dump before disabling the capabilities and detaching; " +
                "Dump works regardless of capability state.",
                "Answers 404 when the tenant is not a child of the current tenant.",
                "Re-attaching with Attach requires the database name (-db); read it from GetTenants before detaching, " +
                "Detach does not print it.",
            ]
        );

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower();

        Logger.LogInformation("Detach tenant \'{TenantId}\' at \'{ServiceClientServiceUri}\'", tenantId,
            ServiceClient.ServiceUri);

        await ServiceClient.DetachTenantAsync(tenantId);

        Logger.LogInformation("Tenant \'{TenantId}\' at \'{ServiceClientServiceUri}\' detached", tenantId,
            ServiceClient.ServiceUri);
    }
}
