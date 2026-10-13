using System.Net;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

/// <summary>
///     Dry-runs or applies the switch of a tenant's pipeline service accounts from stored client
///     secrets to impersonation (AB#5537, CLI surface AB#6642).
///     <para>
///         The dry run is the default and writes nothing. <c>--apply</c> clears stored secrets and
///         invalidates the old ones, so it is gated on <see cref="IConfirmationService" /> like the
///         other destructive verbs.
///     </para>
///     <para>
///         🔴 Only fields of the server report are ever printed. The report carries no client secret
///         and no token, and nothing in this command reads one.
///     </para>
/// </summary>
internal class SwitchServiceAccountsToImpersonationCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IArgument _applyArg;
    private readonly IConfirmationService _confirmationService;
    private readonly IConsoleService _consoleService;
    private readonly IArgument _jsonArg;
    private readonly IArgument _redeployArg;
    private readonly IArgument _yesArg;

    public SwitchServiceAccountsToImpersonationCommand(
        ILogger<SwitchServiceAccountsToImpersonationCommand> logger,
        IOptions<OctoToolOptions> options,
        IConsoleService consoleService,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.CommunicationServicesGroup, "SwitchServiceAccountsToImpersonation",
            "Switches the pipeline service accounts of the tenant from stored client secrets to " +
            "impersonation. Dry run by default; --apply runs the switch.",
            options, communicationServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _confirmationService = confirmationService;
        _applyArg = CommandArgumentValue.AddArgument("a", "apply",
            ["Run the switch (default is a dry run that changes nothing)"], false, 0);
        _redeployArg = CommandArgumentValue.AddArgument("rdf", "redeployDataFlows",
            ["true or false: redeploy the affected data flows after the switch (default true)"], false, 1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
        _jsonArg = CommandArgumentValue.AddArgument("j", "json", ["Output the report as raw JSON"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [], description: "Dry run: show what the switch would do, change nothing"),
                new CodeSample(arguments: [new CodeSampleArgument(_applyArg), new CodeSampleArgument(_yesArg)],
                    description: "Apply the switch without the confirmation prompt (CI/automation)"),
                new CodeSample(arguments: [new CodeSampleArgument(_jsonArg)],
                    description: "Dry run, report as JSON")
            ],
            Notes:
            [
                "Always run the dry run first and read the outcome per account. Without --apply nothing is " +
                "written and no confirmation is asked.",
                "Apply is destructive: stored client secrets are cleared and the old secrets are invalidated " +
                "identity-side. The server refuses it (exit -5) unless the installation sets " +
                "OCTO_SERVICEACCOUNTIMPERSONATION__ALLOWSECRETCLEARING=true.",
                "Two-phase flow: accounts reported as SwitchedPendingInvalidation are switched, but the old " +
                "secret is only invalidated once the data flows are redeployed. Re-run with " +
                "--redeployDataFlows true to complete the switch.",
                "--redeployDataFlows (default true) redeploys the affected data flows after the switch. " +
                "Data flows that fail to redeploy are listed and make the command exit with -5.",
                "A Failed account makes the command exit with -5 after the full report was printed.",
                "The tenant is the tenant of the active context. Neither client secrets nor tokens are ever " +
                "printed: the report contains none."
            ]);

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            throw ToolException.NoTenantIdConfigured();
        }

        var apply = CommandArgumentValue.IsArgumentUsed(_applyArg);
        var redeploy = !CommandArgumentValue.IsArgumentUsed(_redeployArg) ||
                       CommandArgumentValue.GetArgumentScalarValue<bool>(_redeployArg);
        var tenantId = Options.Value.TenantId;

        if (apply && !CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm(
                $"switch the pipeline service accounts of tenant '{tenantId}' to impersonation? " +
                "Stored client secrets are cleared and the old secrets are invalidated"))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation(
            "{Mode} the impersonation switch for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            apply ? "Applying" : "Dry-running", tenantId, ServiceClient.ServiceUri);

        ServiceAccountImpersonationSwitchReportDto report;
        try
        {
            report = await ServiceClient.SwitchServiceAccountsToImpersonationAsync(apply, redeploy);
        }
        catch (ServiceClientResultException ex) when (ex.HttpStatusCode == HttpStatusCode.Conflict)
        {
            throw ToolException.ImpersonationSwitchNotAllowed(ex);
        }

        if (CommandArgumentValue.IsArgumentUsed(_jsonArg))
        {
            _consoleService.WriteLine(JsonConvert.SerializeObject(report));
        }
        else
        {
            PrintReport(report);
        }

        var failedAccounts = report.Accounts
            .Where(a => a.Outcome == ServiceAccountImpersonationSwitchOutcomes.Failed).ToList();
        var failedRedeploys = report.FailedDataFlowRedeploys;
        var pending = report.Accounts
            .Where(a => a.Outcome == ServiceAccountImpersonationSwitchOutcomes.SwitchedPendingInvalidation).ToList();

        if (failedAccounts.Count > 0)
        {
            Logger.LogWarning("{Count} account(s) failed to switch: {Accounts}", failedAccounts.Count,
                string.Join(", ", failedAccounts.Select(a => $"{a.WellKnownName} ({a.Message})")));
        }

        if (failedRedeploys.Count > 0)
        {
            Logger.LogWarning("{Count} data flow(s) failed to redeploy, redeploy them by hand: {DataFlows}",
                failedRedeploys.Count,
                string.Join(", ", failedRedeploys.Select(f => $"{f.DataFlowRtId} ({f.Reason})")));
        }

        if (pending.Count > 0)
        {
            Logger.LogWarning(
                "{Count} account(s) are switched but their old secret is not invalidated yet. Run again with " +
                "--redeployDataFlows true to complete the switch",
                pending.Count);
        }

        if (failedAccounts.Count > 0 || failedRedeploys.Count > 0)
        {
            throw ToolException.ImpersonationSwitchIncomplete(failedAccounts.Count, failedRedeploys.Count);
        }
    }

    private void PrintReport(ServiceAccountImpersonationSwitchReportDto report)
    {
        _consoleService.WriteLine(report.Applied
            ? $"Impersonation switch APPLIED for tenant '{report.TenantId}'."
            : $"Impersonation switch DRY RUN for tenant '{report.TenantId}' — nothing was changed.");

        if (report.Accounts.Count == 0)
        {
            _consoleService.WriteLine("The tenant has no service accounts.");
        }

        foreach (var account in report.Accounts)
        {
            _consoleService.WriteLine(
                $"  {account.WellKnownName}  client={account.ClientId ?? "n/a"}  outcome={account.Outcome}  " +
                $"{account.Message}");
        }

        var counts = report.Accounts
            .GroupBy(a => a.Outcome)
            .ToDictionary(g => g.Key, g => g.Count());
        var ordered = ServiceAccountImpersonationSwitchOutcomes.All.Where(counts.ContainsKey)
            .Concat(counts.Keys.Except(ServiceAccountImpersonationSwitchOutcomes.All).OrderBy(k => k));
        _consoleService.WriteLine("Summary: " + (counts.Count == 0
            ? "no accounts"
            : string.Join(", ", ordered.Select(o => $"{o}={counts[o]}"))));

        if (report.RedeployedDataFlowRtIds.Count > 0)
        {
            _consoleService.WriteLine(
                $"Redeployed data flows ({report.RedeployedDataFlowRtIds.Count}): " +
                string.Join(", ", report.RedeployedDataFlowRtIds));
        }

        foreach (var failure in report.FailedDataFlowRedeploys)
        {
            _consoleService.WriteLine($"  Redeploy FAILED  dataFlow={failure.DataFlowRtId}  {failure.Reason}");
        }
    }
}
