using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

/// <summary>
///     Rotates the client secret of an adapter's pipeline service account (AB#5032, CLI surface
///     AB#5048).
///     <para>
///         Destructive in the only sense that matters here: the moment the controller answers, the
///         previous secret is gone from the identity client, so it is gated on
///         <see cref="IConfirmationService" /> like every other destructive verb.
///     </para>
///     <para>
///         🔴 The command must SHOW the redeploy requirement. The adapter freezes the credentials
///         into the pipeline's <c>GlobalConfiguration</c> at pipeline registration and never
///         refreshes them, so every already-registered pipeline keeps presenting the withdrawn
///         secret until its data flows are redeployed. Swallowing that line is what produces
///         "rotation done, still broken".
///     </para>
/// </summary>
internal class RotateAdapterServiceAccountSecretCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _idArg;
    private readonly IArgument _yesArg;

    public RotateAdapterServiceAccountSecretCommand(ILogger<RotateAdapterServiceAccountSecretCommand> logger,
        IOptions<OctoToolOptions> options,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.CommunicationServicesGroup, "RotateAdapterServiceAccountSecret",
            "Rotates the client secret of an adapter's pipeline service account. The previous secret " +
            "stops working immediately, and the adapter's pipelines / data flows must be redeployed " +
            "afterwards before the new secret takes effect.",
            options, communicationServicesClient, authenticationService)
    {
        _confirmationService = confirmationService;
        _idArg = CommandArgumentValue.AddArgument("id", "identifier", ["The adapter runtime ID"], true, 1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(
                    arguments: [new CodeSampleArgument(_idArg, "69cfa838092b710403248acd")],
                    description: "Rotate the secret (prompts for confirmation)"),
                new CodeSample(
                    arguments:
                    [
                        new CodeSampleArgument(_idArg, "69cfa838092b710403248acd"),
                        new CodeSampleArgument(_yesArg)
                    ],
                    description: "Skip the confirmation prompt (CI/automation)")
            ],
            Notes:
            [
                "Destructive: the previous secret is invalidated immediately. If the call fails, the " +
                "previous secret remains in effect — the controller says so explicitly.",
                "Redeploy the adapter's pipelines / data flows afterwards (`DeployDataFlow` / " +
                "`DeployPipeline`). The adapter caches the credentials at pipeline registration and " +
                "never refreshes them, so until then every pipeline still presents the old secret.",
                "The secret itself is never returned — it lives only in the tenant's service-account " +
                "configuration entity and in the identity client's hash.",
                "A blueprint cannot rotate a live secret: the secret attribute is runtime state, so " +
                "blueprint import/export deliberately leaves it alone. This command is the supported path."
            ]);

    public override async Task Execute()
    {
        var adapterRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_idArg);

        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm(
                $"rotate the pipeline service account secret of adapter '{adapterRtId}' in tenant " +
                $"'{Options.Value.TenantId}'? The current secret stops working immediately, and the " +
                "adapter's pipelines / data flows must be redeployed afterwards"))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation(
            "Rotating the pipeline service account secret of adapter '{AdapterRtId}' for tenant " +
            "'{TenantId}' at '{ServiceClientServiceUri}'",
            adapterRtId, Options.Value.TenantId, ServiceClient.ServiceUri);

        var result = await ServiceClient.RotateServiceAccountSecretAsync(adapterRtId);

        if (result.WasCreated)
        {
            Logger.LogInformation(
                "Adapter '{AdapterRtId}' had no pipeline service account. Client '{ClientId}' " +
                "(configuration '{WellKnownName}') was provisioned instead — nothing was invalidated",
                adapterRtId, result.ClientId, result.ConfigurationWellKnownName);
        }
        else
        {
            Logger.LogInformation(
                "Secret of pipeline service account '{ClientId}' (configuration '{WellKnownName}') rotated",
                result.ClientId, result.ConfigurationWellKnownName);
        }

        // The controller composes this line, redeploy instruction included. Relayed verbatim so the
        // CLI cannot drift from what the server (and the tenant's audit event) states.
        Logger.LogInformation("{Message}", result.Message);

        if (result.RequiresPipelineRedeploy)
        {
            // A warning, not an info line: this is the step people skip, and skipping it leaves every
            // running pipeline authenticating with the secret that was just withdrawn.
            Logger.LogWarning(
                "Redeploy required: the pipelines / data flows of adapter '{AdapterRtId}' still use the " +
                "previous secret until they are redeployed (DeployDataFlow / DeployPipeline). The adapter " +
                "freezes the credentials at pipeline registration and never refreshes them",
                adapterRtId);
        }
    }
}
