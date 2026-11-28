using System.Net;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

/// <summary>
///     Sets the roles a client hands to its mirrors in every child tenant (<c>System.Identity/Client.SetMirrorRoles-1</c>,
///     System.Identity 3.9.0). A dry run by default: it prints the plan per child tenant and writes nothing; <c>--apply</c>
///     writes the list and reconciles the mirrors, and prints the same table with the result. The command ends with a
///     non-zero exit code when a mirror failed (the periodic mirror reconciliation retries it).
/// </summary>
internal class SetClientMirrorRoles : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IdentityMethodCall _methods;
    private readonly IArgument _clientIdArg;
    private readonly IArgument _rolesArg;
    private readonly IArgument _clearArg;
    private readonly IArgument _applyArg;
    private readonly IArgument _jsonArg;

    public SetClientMirrorRoles(ILogger<SetClientMirrorRoles> logger, IOptions<OctoToolOptions> options,
        IConsoleService consoleService, IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "SetClientMirrorRoles",
            "Sets the roles a client hands to its mirrors in every child tenant. Dry run by default: prints the change per child tenant; --apply writes it.",
            options, identityServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _methods = new IdentityMethodCall(tenantClient, authenticationService, logger);
        _clientIdArg = CommandArgumentValue.AddArgument("id", "clientId",
            ["The ClientId of the original client (a confidential client_credentials client of the context's tenant)"],
            true, 1);
        _rolesArg = CommandArgumentValue.AddArgument("r", "roles",
            ["The complete list of role names, separated by commas; it replaces the stored list"], false, 1);
        _clearArg = CommandArgumentValue.AddArgument("clear", "clear",
            ["Clear the list: removes every role identity manages on the mirrors (roles set by hand stay)"], false, 0);
        _applyArg = CommandArgumentValue.AddArgument("apply", "apply",
            ["Write the list and reconcile the mirrors (without it the command is a dry run and writes nothing)"],
            false, 0);
        _jsonArg = CommandArgumentValue.AddArgument("j", "json", ["Output the method result as JSON"], false, 0);
    }

    public override async Task Execute()
    {
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientIdArg);
        var roleNames = RoleNamesOf();
        var apply = CommandArgumentValue.IsArgumentUsed(_applyArg);

        Logger.LogInformation("{Mode} the mirror roles [{Roles}] of client '{ClientId}' at '{ServiceClientServiceUri}'",
            apply ? "Setting" : "Dry run: planning", string.Join(", ", roleNames), clientId, ServiceClient.ServiceUri);

        var result = await _methods.RunAsync(RtClientMethodClient.SetMirrorRolesMethodId,
            async () => await new RtClientMethodClient(_methods.TenantClient).SetMirrorRolesAsync(
                await ResolveClientRtIdAsync(clientId), roleNames, !apply));
        if (result == null)
        {
            throw new ServiceClientException($"Client.SetMirrorRoles-1 returned no result for client '{clientId}'.");
        }

        if (CommandArgumentValue.IsArgumentUsed(_jsonArg))
        {
            _consoleService.WriteLine(JsonConvert.SerializeObject(result, Formatting.Indented));
        }
        else
        {
            foreach (var line in ClientMirrorRolesTable.Format(result))
            {
                _consoleService.WriteLine(line);
            }
        }

        var tenants = result.Tenants?.Where(t => t != null).ToList() ?? [];
        var failed = tenants.Where(t => string.Equals(t!.Outcome, ClientMirrorRolesTable.FailedOutcome,
            StringComparison.OrdinalIgnoreCase)).Select(t => t!.ChildTenantId).ToList();
        if (!apply)
        {
            Logger.LogInformation("Dry run: nothing was written. {Count} child tenant(s) affected; run again with --apply to write the list",
                tenants.Count);
        }

        if (failed.Count > 0)
        {
            throw new ServiceClientException(
                $"The mirror roles of client '{clientId}' could not be {(apply ? "set" : "planned")} in {failed.Count} child tenant(s): " +
                $"{string.Join(", ", failed)}. The periodic mirror reconciliation retries them.");
        }
    }

    /// <summary>
    ///     The runtime id of the original client; an unknown client ends the command before any call with a message naming
    ///     client and tenant (exit -5).
    /// </summary>
    private async Task<string> ResolveClientRtIdAsync(string clientId)
    {
        try
        {
            return await IdentityMethodCall.ResolveClientRtIdAsync(ServiceClient, clientId);
        }
        catch (ServiceClientResultException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            throw new ToolException($"Client '{clientId}' was not found in tenant '{Options.Value.TenantId}'.");
        }
    }

    private List<string> RoleNamesOf()
    {
        var clear = CommandArgumentValue.IsArgumentUsed(_clearArg);
        var hasRoles = CommandArgumentValue.IsArgumentUsed(_rolesArg);
        if (clear == hasRoles)
        {
            throw new ToolException("Pass either --roles <name>[,<name>] or --clear.");
        }

        if (clear)
        {
            return [];
        }

        var names = CommandArgumentValue.GetArgumentScalarValue<string>(_rolesArg)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return names.Count == 0
            ? throw new ToolException("--roles names no role; pass --clear to clear the list.")
            : names;
    }
}
