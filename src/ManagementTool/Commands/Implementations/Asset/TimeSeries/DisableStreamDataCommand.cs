using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

public class DisableStreamDataCommand : ServiceClientOctoCommand<IStreamDataServicesClient>
{
    public DisableStreamDataCommand(ILogger<DisableStreamDataCommand> logger,
        IOptions<OctoToolOptions> options, IStreamDataServicesClient serviceClient,
        IAuthenticationService authenticationService) : base(logger, Constants.AssetRepositoryServicesGroup,
        "DisableStreamData",
        "Disable stream data services for the current tenant.", options,
        serviceClient, authenticationService)
    {
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [], description: "Basic usage"),
            ],
            Notes:
            [
                "Refused with HTTP 409 while archives of the tenant are still activated; the error names them. " +
                "Disable them first with DisableArchive (data is kept) or remove them with DeleteArchive (rollups " +
                "before their source archive) - like this command they act on the tenant of the active context " +
                "(UseContext or --context <name>).",
                "Disabling only switches the tenant flag off: the System.StreamData CK model, the archive definitions " +
                "and the stored stream data stay and are usable again after EnableStreamData. Disabling Stream Data " +
                "is a precondition for Delete and Detach of the tenant (AB#4255); Delete then drops the CrateDB " +
                "tables of the tenant's archives, Detach keeps them.",
                "Reversible with EnableStreamData.",
            ]
        );

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }


        Logger.LogInformation("Disable stream data for tenant \'{TenantId}\' at \'{ServiceClientServiceUri}\'",
            Options.Value.TenantId,
            ServiceClient.ServiceUri);

        await ServiceClient.DisableAsync(Options.Value.TenantId);

        Logger.LogInformation("Stream data for tenant \'{ClientId}\' at \'{ServiceClientServiceUri}\' disabled",
            Options.Value.TenantId,
            ServiceClient.ServiceUri);
    }
}