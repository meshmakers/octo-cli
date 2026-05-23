using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class MovePipelinesToAdapterCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _pipelineRtIds;
    private readonly IArgument _targetAdapterRtId;
    private readonly IArgument _redeployArg;
    private readonly IArgument _yesArg;

    public MovePipelinesToAdapterCommand(ILogger<MovePipelinesToAdapterCommand> logger,
        IOptions<OctoToolOptions> options,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.CommunicationServicesGroup, "MovePipelines",
            "Reassigns one or more pipelines from their current adapter to a new target adapter. " +
            "Each pipeline is moved atomically; per-pipeline failures do not abort the batch. " +
            "Source and target adapter must share the same CkTypeId.",
            options, communicationServicesClient, authenticationService)
    {
        _confirmationService = confirmationService;

        _pipelineRtIds = CommandArgumentValue.AddArgument("ids", "pipelineRtIds",
            ["Comma-separated list of pipeline runtime object IDs to move"], true, 1);
        _targetAdapterRtId = CommandArgumentValue.AddArgument("aid", "targetAdapterRtId",
            ["Runtime object ID of the new owning adapter"], true, 1);
        _redeployArg = CommandArgumentValue.AddArgument("rd", "redeploy",
            ["Optional: re-deploy each pipeline onto the new adapter after the move. " +
             "A redeploy failure does not roll the move back — the pipeline already points at " +
             "the new adapter, deploy can be retried manually."], false, 0);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override async Task Execute()
    {
        var pipelineRtIdsRaw = CommandArgumentValue.GetArgumentScalarValue<string>(_pipelineRtIds);
        var targetAdapterRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_targetAdapterRtId);
        var redeploy = CommandArgumentValue.IsArgumentUsed(_redeployArg);

        var pipelineRtIds = pipelineRtIdsRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (pipelineRtIds.Count == 0)
        {
            Logger.LogError("No pipeline IDs supplied — -ids must contain at least one entry");
            throw ToolException.OperationCancelledByUser();
        }

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg))
        {
            var redeployHint = redeploy ? " + redeploy onto new adapter" : string.Empty;
            if (!_confirmationService.Confirm(
                    $"Move {pipelineRtIds.Count} pipeline(s) to adapter '{targetAdapterRtId}'{redeployHint}?"))
            {
                throw ToolException.OperationCancelledByUser();
            }
        }

        Logger.LogInformation(
            "Moving {Count} pipeline(s) to adapter '{TargetAdapterRtId}' (redeploy={Redeploy}) " +
            "for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            pipelineRtIds.Count, targetAdapterRtId, redeploy, Options.Value.TenantId, ServiceClient.ServiceUri);

        var response = await ServiceClient.MovePipelinesToAdapterAsync(
            new MovePipelinesToAdapterRequestDto(pipelineRtIds, targetAdapterRtId, redeploy));

        var successCount = 0;
        var failureCount = 0;
        foreach (var result in response.Results)
        {
            if (result.Success)
            {
                successCount++;
                if (!string.IsNullOrEmpty(result.ErrorMessage))
                {
                    Logger.LogWarning("Pipeline {PipelineRtId} moved {From} -> {To} but: {Warning}",
                        result.PipelineRtId, result.OldAdapterRtId, result.NewAdapterRtId, result.ErrorMessage);
                }
                else
                {
                    Logger.LogInformation("Pipeline {PipelineRtId} moved {From} -> {To}",
                        result.PipelineRtId, result.OldAdapterRtId, result.NewAdapterRtId);
                }
            }
            else
            {
                failureCount++;
                Logger.LogError("Pipeline {PipelineRtId} move FAILED: {Error}",
                    result.PipelineRtId, result.ErrorMessage);
            }
        }

        Logger.LogInformation("Move summary: {Success} succeeded, {Failure} failed (of {Total})",
            successCount, failureCount, response.Results.Count);

        if (failureCount > 0)
        {
            throw new ToolException($"{failureCount} pipeline move(s) failed — see log above");
        }
    }
}
