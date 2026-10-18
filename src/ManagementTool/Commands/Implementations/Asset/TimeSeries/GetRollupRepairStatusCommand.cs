using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

/// <summary>
///     Reads the state of a rollup repair request that <c>RepairRollups --apply</c> started (AB#6968). Read only.
/// </summary>
internal class GetRollupRepairStatusCommand : ServiceClientOctoCommand<IStreamDataServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _failOnFailedArg;
    private readonly IArgument _jsonArg;
    private readonly IArgument _requestIdArg;

    public GetRollupRepairStatusCommand(ILogger<GetRollupRepairStatusCommand> logger,
        IOptions<OctoToolOptions> options, IConsoleService consoleService, IStreamDataServicesClient serviceClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "GetRollupRepairStatus",
            "Shows the state (Pending, Running, Done, Failed) of a rollup repair request started by RepairRollups --apply, with the final report once it has ended. Read only.",
            options, serviceClient, authenticationService)
    {
        _consoleService = consoleService;
        _requestIdArg = CommandArgumentValue.AddArgument("rid", "requestId",
            ["Id of the repair request, printed by RepairRollups --apply"], true, 1);
        _failOnFailedArg = CommandArgumentValue.AddArgument("ff", "failOnFailed",
            ["Exit with a non-zero code when the request has Failed (for scripts)"], false, 0);
        _jsonArg = CommandArgumentValue.AddArgument("j", "json", ["Output the request as raw JSON"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_requestIdArg, "req-1")],
                    description: "Show the state of a repair request"),
                new CodeSample(
                    arguments: [new CodeSampleArgument(_requestIdArg, "req-1"), new CodeSampleArgument(_failOnFailedArg)],
                    description: "Show the state and exit non-zero when the repair has failed")
            ],
            Notes:
            [
                "A request ends in Done or Failed. The final report lists every step, the validation after the repair and, on failure, the failed step.",
                "The tenant is the tenant of the active context."
            ]);

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            throw ToolException.NoTenantIdConfigured();
        }

        var tenantId = Options.Value.TenantId;
        var requestId = CommandArgumentValue.GetArgumentScalarValue<string>(_requestIdArg);

        var request = await ServiceClient.GetRollupRepairRequestAsync(tenantId, requestId);

        if (CommandArgumentValue.IsArgumentUsed(_jsonArg))
        {
            _consoleService.WriteLine(JsonConvert.SerializeObject(request, Formatting.Indented));
        }
        else
        {
            RollupConsistencyFormatter.Print(_consoleService, request);
        }

        if (CommandArgumentValue.IsArgumentUsed(_failOnFailedArg))
        {
            RollupRepairRequestOutcome.ThrowIfFailed(request);
        }
    }
}
