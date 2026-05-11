using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

public class UnfreezeRollupArchiveCommand : ServiceClientOctoCommand<IStreamDataServicesClient>
{
    private readonly IArgument _rollupRtIdArg;
    private readonly IArgument _acceptGapsArg;

    public UnfreezeRollupArchiveCommand(ILogger<UnfreezeRollupArchiveCommand> logger,
        IOptions<OctoToolOptions> options, IStreamDataServicesClient serviceClient,
        IAuthenticationService authenticationService) : base(logger, Constants.AssetRepositoryServicesGroup,
        "UnfreezeRollupArchive",
        "Clears FrozenUntil on a CkRollupArchive. Idempotent. Pass --acceptGaps when source data inside the previously frozen range has been truncated and the resulting gaps are acceptable.",
        options, serviceClient, authenticationService)
    {
        _rollupRtIdArg = CommandArgumentValue.AddArgument("id", "identifier",
            ["Runtime id of the CkRollupArchive entity to unfreeze"], true, 1);
        _acceptGapsArg = CommandArgumentValue.AddArgument("ag", "acceptGaps",
            ["Acknowledge that unfreezing may produce visible gaps once the orchestrator catches up."], false, 0);
    }

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var rollupRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_rollupRtIdArg);
        var acceptGaps = CommandArgumentValue.IsArgumentUsed(_acceptGapsArg);

        Logger.LogInformation(
            "Unfreezing rollup archive '{RollupRtId}' for tenant '{TenantId}' (acceptGaps={AcceptGaps})",
            rollupRtId, Options.Value.TenantId, acceptGaps);

        await ServiceClient.UnfreezeRollupArchiveAsync(Options.Value.TenantId, rollupRtId, acceptGaps);

        Logger.LogInformation("Rollup archive '{RollupRtId}' unfrozen", rollupRtId);
    }
}
