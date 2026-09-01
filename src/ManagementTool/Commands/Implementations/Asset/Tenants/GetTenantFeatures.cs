using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;

// Public (not internal like its siblings) so the output contract can be pinned in ManagementTool.Tests.
public class GetTenantFeatures : ServiceClientOctoCommand<IAssetServicesClient>
{
    private const int CapabilityColumnWidth = 20;

    private readonly IConsoleService _consoleService;

    public GetTenantFeatures(ILogger<GetTenantFeatures> logger,
        IConsoleService consoleService,
        IOptions<OctoToolOptions> options, IAssetServicesClient assetServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "GetTenantFeatures",
            "Gets the enabled-state of the current tenant's capabilities (Stream Data, Communication, Reporting, AI Services).",
            options, assetServicesClient, authenticationService)
    {
        _consoleService = consoleService;
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [], description: "Basic usage",
                    expectedOutput: """
                    CAPABILITY          STATE
                    Stream Data         Enabled
                    Communication       Enabled
                    Reporting           Disabled
                    AI Services         Disabled
                    """),
            ],
            Notes:
            [
                "Reports the same per-tenant capability flags the delete/detach guard evaluates (AB#4255): all four " +
                "must read Disabled before Delete or Detach of the tenant is accepted. Like the Enable*/Disable* " +
                "commands it acts on the tenant of the active context (UseContext or --context <name>).",
                "For Stream Data the tenant flag is reported as-is even when stream data is switched off at the " +
                "instance level; a note on the line surfaces that case.",
                "The flags are toggled with EnableStreamData/DisableStreamData, EnableCommunication/" +
                "DisableCommunication, EnableReporting/DisableReporting and EnableAi/DisableAi.",
            ]
        );

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        Logger.LogInformation("Getting tenant features for tenant \'{TenantId}\' at \'{ServiceClientServiceUri}\'",
            Options.Value.TenantId,
            ServiceClient.ServiceUri);

        var status = await ServiceClient.GetTenantFeaturesStatusAsync();

        _consoleService.WriteColumnLine("CAPABILITY", CapabilityColumnWidth, "STATE");
        _consoleService.WriteColumnLine("Stream Data", CapabilityColumnWidth, StreamDataState(status.StreamData));
        _consoleService.WriteColumnLine("Communication", CapabilityColumnWidth, State(status.Communication?.TenantEnabled));
        _consoleService.WriteColumnLine("Reporting", CapabilityColumnWidth, State(status.Reporting?.TenantEnabled));
        _consoleService.WriteColumnLine("AI Services", CapabilityColumnWidth, State(status.AiServices?.TenantEnabled));
    }

    private static string StreamDataState(StreamDataFeatureStatusDto? streamData)
    {
        var state = State(streamData?.TenantEnabled);
        // The tenant flag is reported regardless of the instance flag — a tenant left enabled on an
        // installation without stream data shows as exactly that.
        return streamData is { InstanceEnabled: false }
            ? state + " (stream data is switched off at the instance level)"
            : state;
    }

    private static string State(bool? tenantEnabled) => tenantEnabled == true ? "Enabled" : "Disabled";
}
