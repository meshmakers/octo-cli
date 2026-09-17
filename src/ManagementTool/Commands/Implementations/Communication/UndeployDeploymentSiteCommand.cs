using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class UndeployDeploymentSiteCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _deploymentSiteRtId;
    private readonly IArgument _yesArg;

    public UndeployDeploymentSiteCommand(ILogger<UndeployDeploymentSiteCommand> logger, IOptions<OctoToolOptions> options,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.CommunicationServicesGroup, "UndeployDeploymentSite",
            "Undeploys a deployment site through the Communication Operator. Undeploy the deployment site's workloads first (UndeployWorkload); Cloud deployment sites release their operator resources.",
            options, communicationServicesClient, authenticationService)
    {
        _confirmationService = confirmationService;

        _deploymentSiteRtId = CommandArgumentValue.AddArgument("id", "deploymentSiteRtId",
            ["The deployment site's runtime object ID"], true, 1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_deploymentSiteRtId, "670000000000000000000001")],
                    description: "Basic usage"),
                new CodeSample(arguments:
                    [
                        new CodeSampleArgument(_deploymentSiteRtId, "670000000000000000000001"),
                        new CodeSampleArgument(_yesArg),
                    ],
                    description: "Non-interactive"),
            ],
            Notes:
            [
                "Undeploy the deployment site's workloads (Adapters and Applications) with UndeployWorkload before the deployment site itself; " +
                "the operator removes the deployment site resources (DeploymentSite resource and broker secret for Cloud deployment sites) " +
                "once nothing runs in it.",
                "Required before DisableCommunication, which is refused with HTTP 409 while any deployment site or workload of the " +
                "tenant is still deployed (AB#4255).",
                "Acts on the tenant of the active context; switch with UseContext or pass --context <name>.",
            ]
        );

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var deploymentSiteRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_deploymentSiteRtId);

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm(
                $"Undeploy deployment site '{deploymentSiteRtId}' of tenant '{Options.Value.TenantId}'? The operator will remove its resources."))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation(
            "Undeploying deployment site '{PoolRtId}' for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            deploymentSiteRtId, Options.Value.TenantId, ServiceClient.ServiceUri);

        await ServiceClient.UndeployDeploymentSiteAsync(deploymentSiteRtId);

        Logger.LogInformation("Deployment site '{PoolRtId}' undeploy triggered", deploymentSiteRtId);
    }
}
