using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services.WorkloadResources;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

/// <summary>
///     Shows and sets the typed CPU and memory settings of one adapter or application (AB#7429): CpuRequest, CpuLimit,
///     MemoryRequest, MemoryLimit. Writes only runtime state and never deploys.
/// </summary>
internal class UpdateWorkloadResourcesCommand : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _cpuLimit;
    private readonly IArgument _cpuRequest;
    private readonly IArgument _memoryLimit;
    private readonly IArgument _memoryRequest;
    private readonly IArgument _workloadRtId;

    public UpdateWorkloadResourcesCommand(ILogger<UpdateWorkloadResourcesCommand> logger,
        IOptions<OctoToolOptions> options, IConsoleService consoleService,
        IAssetServicesClient assetServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "UpdateWorkloadResources",
            "Shows and sets the CPU and memory requests and limits of an adapter or application. " +
            "Does NOT trigger a deploy — call DeployWorkload afterwards.",
            options, assetServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _workloadRtId = CommandArgumentValue.AddArgument("id", "workloadRtId",
            ["The runtime object ID of the adapter or application"], true, 1);
        _cpuRequest = CommandArgumentValue.AddArgument("cr", "cpu-request",
            ["CPU request, a Kubernetes quantity such as '250m' or '0.5'. Without a value (or with '') it is cleared"], false, 0, true);
        _cpuLimit = CommandArgumentValue.AddArgument("cl", "cpu-limit",
            ["CPU limit, a Kubernetes quantity such as '1' or '1500m'. Without a value (or with '') it is cleared"], false, 0, true);
        _memoryRequest = CommandArgumentValue.AddArgument("mr", "memory-request",
            ["Memory request, a Kubernetes quantity such as '512Mi' or '1Gi'. Without a value (or with '') it is cleared"], false, 0, true);
        _memoryLimit = CommandArgumentValue.AddArgument("ml", "memory-limit",
            ["Memory limit, a Kubernetes quantity such as '1Gi' or '3Gi'. Without a value (or with '') it is cleared"], false, 0, true);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_workloadRtId, "69cfa838092b710403248acd")],
                    description: "Shows the current values (also the way to read them for an application)"),
                new CodeSample(arguments:
                    [
                        new CodeSampleArgument(_workloadRtId, "69cfa838092b710403248acd"),
                        new CodeSampleArgument(_memoryRequest, "512Mi")
                    ],
                    description: "Sets the memory request; the other three values stay as they are"),
                new CodeSample(arguments:
                    [
                        new CodeSampleArgument(_workloadRtId, "69cfa838092b710403248acd"),
                        new CodeSampleArgument(_memoryLimit)
                    ],
                    description: "Clears the memory limit (the chart default applies again)")
            ],
            Notes:
            [
                "- Without any value option the command only shows the current values. An unset value is shown as 'chart default'.",
                "- An option without a value (or with an empty string) clears that attribute; the other three stay as they are.",
                "- Quantities and request <= limit are checked here with the same messages the communication controller uses at DeployWorkload; nothing is written when a check fails.",
                "- The values reach Kubernetes with the next DeployWorkload. Precedence: chart default < these settings < explicit valuesYaml / values[] overrides of the workload; an explicit override wins and the controller reports it in LastDeploymentWarning.",
                "- Adapter pools are sized with their PoolMember* settings, not with this command.",
                "- Needs System.Communication 4.10.0 or newer in the tenant."
            ]
        );

    public override async Task Execute()
    {
        var tenantId = Options.Value.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw ToolException.NoTenantIdConfigured();
        }

        tenantId = tenantId.Trim().ToLowerInvariant();
        var rtId = CommandArgumentValue.GetArgumentScalarValue<string>(_workloadRtId).Trim();

        var changes = CollectChanges();

        var access = new WorkloadResourcesAccess(ServiceClient);
        var workload = await access.FindAsync(tenantId, rtId)
                       ?? throw new ToolException(
                           $"No adapter or application with id '{rtId}' in tenant '{tenantId}'. " +
                           "Adapter pools are sized with their PoolMember* settings.");

        if (changes.Count == 0)
        {
            WriteState(workload);
            return;
        }

        var target = workload.Settings;
        foreach (var (name, value) in changes)
        {
            target = target.With(name, value);
        }

        var errors = target.Validate();
        if (errors.Count > 0)
        {
            throw new ToolException(
                $"Nothing was written. {string.Join("; ", errors)}.");
        }

        Logger.LogInformation(
            "Setting {Attributes} of {Kind} '{WorkloadRtId}' for tenant '{TenantId}'",
            string.Join(", ", changes.Keys), workload.Kind, rtId, tenantId);

        var updated = await access.UpdateAsync(tenantId, workload, changes);
        WriteState(updated);
        _consoleService.WriteLine(
            $"Resources updated. Nothing was deployed — run 'DeployWorkload -id {rtId}' to apply them.");
    }

    /// <summary>The values given on the command line; an empty value means "clear" and maps to <c>null</c>.</summary>
    private Dictionary<string, string?> CollectChanges()
    {
        var changes = new Dictionary<string, string?>(StringComparer.Ordinal);
        Add(changes, "CpuRequest", _cpuRequest);
        Add(changes, "CpuLimit", _cpuLimit);
        Add(changes, "MemoryRequest", _memoryRequest);
        Add(changes, "MemoryLimit", _memoryLimit);
        return changes;
    }

    private void Add(Dictionary<string, string?> changes, string attributeName, IArgument argument)
    {
        if (!CommandArgumentValue.IsArgumentUsed(argument))
        {
            return;
        }

        // The parser drops an empty string, so "--memory-limit ''" arrives as the flag without a value: that is "clear".
        var values = CommandArgumentValue.GetArgumentValue(argument).Values;
        var value = values.Count > 0 ? values[0].Trim() : null;
        changes[attributeName] = string.IsNullOrEmpty(value) ? null : value;
    }

    private void WriteState(WorkloadResourcesState state)
    {
        _consoleService.WriteLine($"{state.Kind} '{state.Name}' ({state.RtId})");
        foreach (var name in WorkloadResourceSettings.AttributeNames)
        {
            _consoleService.WriteLine($"  {name + ":",-15} {state.Settings.Get(name) ?? "(chart default)"}");
        }

        _consoleService.WriteLine($"  {"LastDeploymentWarning:",-15} {state.Settings.LastDeploymentWarning ?? "(none)"}");
    }
}
