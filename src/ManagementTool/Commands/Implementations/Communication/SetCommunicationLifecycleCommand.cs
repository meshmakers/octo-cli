using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

/// <summary>
///     Sets the tenant's communication lifecycle switches (AB#4914 scale-to-zero, AB#4924 leasing).
/// </summary>
/// <remarks>
///     🔴 <b>The PUT replaces the whole record</b>, so a flag that is not given would be reset to its
///     default. Both arguments are therefore optional and the command reads the current record first,
///     overwriting only what it was actually given. With one flag the question could not arise; with
///     two, "turn leasing off" must not silently un-hibernate the tenant's OnDemand workloads as a
///     side effect.
/// </remarks>
internal class SetCommunicationLifecycleCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IArgument _leasingEnabledArg;
    private readonly IArgument _scaleToZeroEnabledArg;

    public SetCommunicationLifecycleCommand(ILogger<SetCommunicationLifecycleCommand> logger,
        IOptions<OctoToolOptions> options,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "SetCommunicationLifecycle",
            "Sets the tenant's communication lifecycle configuration: scale-to-zero (AB#4914) and " +
            "adapter pool leasing (AB#4924). Runtime configuration - effective without a controller " +
            "redeploy; '-sze false' and '-le false' are the per-tenant emergency stops. Only the flags " +
            "you pass are changed.", options,
            communicationServicesClient, authenticationService)
    {
        _scaleToZeroEnabledArg = CommandArgumentValue.AddArgument("sze", "scaleToZeroEnabled",
            ["true to allow OnDemand workloads of this tenant to scale to 0 replicas when idle, false to disable (default)"],
            false, 1);
        _leasingEnabledArg = CommandArgumentValue.AddArgument("le", "leasingEnabled",
            [
                "true to allow adapter pool leasing for this tenant, false to disable (default). Governs BOTH halves: " +
                "this tenant's pools lending their members out, and this tenant's Leased adapters being scheduled. " +
                "A lease needs it on for the lending AND the borrowing tenant. Switching it off HOLDS the queue - " +
                "already queued work stays queued and visible, nothing new is enqueued and no lease is granted"
            ],
            false, 1);
    }

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var setsScaleToZero = CommandArgumentValue.IsArgumentUsed(_scaleToZeroEnabledArg);
        var setsLeasing = CommandArgumentValue.IsArgumentUsed(_leasingEnabledArg);

        if (!setsScaleToZero && !setsLeasing)
        {
            // Refused rather than treated as "set everything to its default": a bare
            // SetCommunicationLifecycle that silently turned both switches off would be the worst
            // possible reading of an empty command line.
            Logger.LogError(
                "Nothing to set. Pass -sze <true|false> and/or -le <true|false>; omitted flags are left unchanged.");
            return;
        }

        // 🔴 Read first. The endpoint replaces the record, so an omitted flag would otherwise be reset.
        var current = await ServiceClient.GetLifecycleAsync();

        var scaleToZeroEnabled = setsScaleToZero
            ? CommandArgumentValue.GetArgumentScalarValue<bool>(_scaleToZeroEnabledArg)
            : current.ScaleToZeroEnabled;
        var leasingEnabled = setsLeasing
            ? CommandArgumentValue.GetArgumentScalarValue<bool>(_leasingEnabledArg)
            : current.LeasingEnabled;

        Logger.LogInformation(
            "Setting communication lifecycle for tenant '{TenantId}' at '{ServiceClientServiceUri}': " +
            "ScaleToZeroEnabled={ScaleToZeroEnabled}, LeasingEnabled={LeasingEnabled}",
            Options.Value.TenantId, ServiceClient.ServiceUri, scaleToZeroEnabled, leasingEnabled);

        var result = await ServiceClient.SetLifecycleAsync(
            new CommunicationLifecycleDto(scaleToZeroEnabled, leasingEnabled));

        Logger.LogInformation(
            "Communication lifecycle for tenant '{TenantId}' updated: ScaleToZeroEnabled={ScaleToZeroEnabled}, " +
            "LeasingEnabled={LeasingEnabled}",
            Options.Value.TenantId, result.ScaleToZeroEnabled, result.LeasingEnabled);
    }
}
