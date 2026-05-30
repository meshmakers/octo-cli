using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class SetPipelineDebugCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IArgument _idArg;
    private readonly IArgument _enabledArg;

    public SetPipelineDebugCommand(ILogger<SetPipelineDebugCommand> logger, IOptions<OctoToolOptions> options,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "SetPipelineDebug",
            "Enables or disables debug capture for a pipeline.", options,
            communicationServicesClient, authenticationService)
    {
        _idArg = CommandArgumentValue.AddArgument("id", "identifier", ["The pipeline runtime ID"], true, 1);
        _enabledArg = CommandArgumentValue.AddArgument("e", "enabled",
            ["true to enable debug capture, false to disable"], true, 1);
    }

    public override async Task Execute()
    {
        var id = CommandArgumentValue.GetArgumentScalarValue<string>(_idArg);
        var enabled = CommandArgumentValue.GetArgumentScalarValue<bool>(_enabledArg);

        Logger.LogInformation(
            "Setting pipeline '{PipelineId}' debugging to {Enabled} for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            id, enabled, Options.Value.TenantId, ServiceClient.ServiceUri);

        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var result = await ServiceClient.SetPipelineDebuggingAsync(id, enabled);

        if (result.AppliedToRunningAdapter)
        {
            Logger.LogInformation(
                "Pipeline '{PipelineId}' debugging set to {Enabled} and applied to the running adapter",
                id, result.Enabled);
        }
        else
        {
            Logger.LogWarning(
                "Pipeline '{PipelineId}' debugging set to {Enabled}; the adapter is offline, so it applies on the next deploy",
                id, result.Enabled);
        }
    }
}
