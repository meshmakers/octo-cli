using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using GraphQlDtos.DataTransferObjects.System.Communication.v4;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class UndeployDataFlowCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly CommunicationMethodCall _methods;
    private readonly IArgument _idArg;

    public UndeployDataFlowCommand(ILogger<UndeployDataFlowCommand> logger, IOptions<OctoToolOptions> options,
        ICommunicationServicesClient communicationServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "UndeployDataFlow",
            "Undeploys a specific data flow.", options,
            communicationServicesClient, authenticationService)
    {
        _methods = new CommunicationMethodCall(tenantClient, authenticationService, logger);
        _idArg = CommandArgumentValue.AddArgument("id", "identifier", ["The data flow runtime ID"], true, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_idArg, "cc0000000000000000000002")], description: "Basic usage"),
            ]
        );

    public override async Task Execute()
    {
        var id = CommandArgumentValue.GetArgumentScalarValue<string>(_idArg);

        Logger.LogInformation(
            "Undeploying data flow '{DataFlowId}' for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            id, Options.Value.TenantId, ServiceClient.ServiceUri);

        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        CommunicationMethodCall.RequireObjectId(id, "DataFlowRtId");
        await _methods.RunAsync(RtDataFlowMethodClient.UndeployMethodId,
            () => new RtDataFlowMethodClient(_methods.TenantClient).UndeployAsync(id));

        Logger.LogInformation("Data flow '{DataFlowId}' undeployed successfully for tenant '{TenantId}'",
            id, Options.Value.TenantId);
    }
}
