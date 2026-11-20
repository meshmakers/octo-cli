using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using GraphQlDtos.DataTransferObjects.System.Communication.v4;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class MovePipelinesToAdapterCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly CommunicationMethodCall _methods;
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _pipelineRtIds;
    private readonly IArgument _targetAdapterRtId;
    private readonly IArgument _redeployArg;
    private readonly IArgument _yesArg;

    public MovePipelinesToAdapterCommand(ILogger<MovePipelinesToAdapterCommand> logger,
        IOptions<OctoToolOptions> options,
        ICommunicationServicesClient communicationServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.CommunicationServicesGroup, "MovePipelines",
            "Reassigns one or more pipelines from their current adapter to a new target adapter. " +
            "Each pipeline is moved atomically; per-pipeline failures do not abort the batch. " +
            "Source and target adapter must share the same CkTypeId.",
            options, communicationServicesClient, authenticationService)
    {
        _methods = new CommunicationMethodCall(tenantClient, authenticationService, logger);
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

        // One method call per pipeline, in the order given; a refused pipeline is reported and the others still move, as
        // the REST list did.
        CommunicationMethodCall.RequireObjectId(targetAdapterRtId, "TargetAdapterRtId");
        var results = new List<MovePipelineResultDto>(pipelineRtIds.Count);
        foreach (var pipelineRtId in pipelineRtIds)
        {
            results.Add(await MoveOneAsync(pipelineRtId, targetAdapterRtId, redeploy));
        }

        var successCount = 0;
        var failureCount = 0;
        foreach (var result in results)
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
            successCount, failureCount, results.Count);

        if (failureCount > 0)
        {
            throw new ToolException($"{failureCount} pipeline move(s) failed — see log above");
        }
    }

    /// <summary>
    ///     Moves one pipeline through <c>Pipeline.MoveToAdapter-1</c> and returns the line the REST list answered for it,
    ///     as the controller's facade maps it: a declared error, an unknown pipeline and every other method error (e.g.
    ///     <c>METHOD_TIMEOUT</c>, <c>METHOD_HANDLER_FAILED</c>) is a failed item and the other pipelines still move; only a
    ///     refusal of the caller (<c>METHOD_FORBIDDEN</c>, the same for every pipeline) or a service without the method
    ///     ends the whole command.
    /// </summary>
    private async Task<MovePipelineResultDto> MoveOneAsync(string pipelineRtId, string targetAdapterRtId, bool redeploy)
    {
        if (!CommunicationMethodCall.IsObjectId(pipelineRtId))
        {
            return new MovePipelineResultDto(pipelineRtId, false, null, null,
                $"PipelineRtId '{pipelineRtId}' is not a valid id");
        }

        try
        {
            var moved = await _methods.InvokeAsync(RtPipelineMethodClient.MoveToAdapterMethodId,
                () => new RtPipelineMethodClient(_methods.TenantClient)
                    .MoveToAdapterAsync(pipelineRtId, targetAdapterRtId, redeploy));
            return moved.IsSuccess
                ? new MovePipelineResultDto(pipelineRtId, true, moved.Value?.OldAdapterRtId,
                    moved.Value?.NewAdapterRtId, moved.Value?.Warning)
                : new MovePipelineResultDto(pipelineRtId, false, null, null,
                    CkMethodErrorText.Format(moved.Error!));
        }
        catch (CkMethodCallException e) when (e.Code == CommunicationMethodCall.TargetNotFoundCode)
        {
            return new MovePipelineResultDto(pipelineRtId, false, null, null,
                $"Pipeline '{pipelineRtId}' was not found");
        }
        catch (CkMethodCallException e) when (e.Code != CommunicationMethodCall.ForbiddenCode)
        {
            return new MovePipelineResultDto(pipelineRtId, false, null, null, CkMethodErrorText.Format(e));
        }
    }
}
