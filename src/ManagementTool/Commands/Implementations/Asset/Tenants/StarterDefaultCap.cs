using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AiServices;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;

/// <summary>
///     AB#8452: the default monthly LLM cap of a starter tenant (AI service, <c>POST
///     system/v1/aiSpendCaps/&lt;tenant&gt;/starterDefault</c>), set by <c>Create --blueprint System.Ai.Starter-*</c> and by
///     <c>InstallBlueprint --child-tenant-id</c> from the system tenant, as MCP <c>create_tenant starter=true</c> does.
///     One rule set for both commands: only for the starter blueprint, only from the system tenant, only with a person
///     login (the endpoint refuses service tokens); the endpoint creates the cap once and never changes it.
/// </summary>
internal static class StarterDefaultCap
{
    /// <summary>Blueprint name (without version) of the starter tenant.</summary>
    internal const string StarterBlueprintName = "System.Ai.Starter";

    /// <summary>True for <c>System.Ai.Starter-&lt;version&gt;</c> (name compared without case).</summary>
    internal static bool IsStarterBlueprint(string blueprintId) =>
        blueprintId.StartsWith(StarterBlueprintName + "-", StringComparison.OrdinalIgnoreCase) &&
        blueprintId.Length > StarterBlueprintName.Length + 1;

    /// <summary>
    ///     A client-credentials session has no refresh token (the convention of <see cref="ChildTenantAccess" />); a
    ///     person login (device code) has one.
    /// </summary>
    internal static bool IsServiceSession(OctoToolAuthenticationOptions authentication) =>
        string.IsNullOrEmpty(authentication.RefreshToken);

    /// <summary>
    ///     The warning of a client-credentials session, which does not call the endpoint.
    ///     <paramref name="subject" /> says what happened, e.g. <c>Tenant 'x' was created</c>.
    /// </summary>
    internal static void WarnSkipped(ILogger logger, string subject, string tenantId)
    {
        // The endpoint refuses service tokens (PersonTokenRequirement): a mirrored client's credentials are also its
        // parent's, so a client token of the system tenant does not prove who calls.
        logger.LogWarning(
            "{Subject} without its default LLM cap: the starter cap needs a person login of the system tenant, this is " +
            "a client-credentials session. A platform admin sets it with 'SetAiSpendCap -t {TenantId} -c <EUR> -ks " +
            "CentralKey -r <reason>'", subject, tenantId);
    }

    /// <summary>
    ///     Calls the endpoint for <paramref name="tenantId" /> and logs the outcome. A failed call is logged with
    ///     <paramref name="subject" /> (what already exists) and the remedy, plus <paramref name="furtherRemedy" />, and
    ///     rethrown, so the command keeps the call's exit code.
    /// </summary>
    internal static async Task ApplyAsync(ILogger logger, IAiServicesClient aiServicesClient,
        IAuthenticationService authenticationService, string tenantId, string subject, string furtherRemedy = "")
    {
        logger.LogInformation("Setting the default LLM cap of starter tenant '{TenantId}' at '{AiServiceUri}'", tenantId,
            aiServicesClient.ServiceUri);

        StarterSpendCapResultDto result;
        try
        {
            await authenticationService.EnsureAuthenticated(aiServicesClient.AccessToken);
            result = await aiServicesClient.ApplyStarterDefaultSpendCapAsync(tenantId);
        }
        catch (Exception e) when (e is ServiceClientException or ServiceClientResultException or HttpRequestException)
        {
            logger.LogError(
                "{Subject}, but setting its default LLM cap failed; it stays without central-key spend until a cap is " +
                "set. Fix the cause below, then run 'SetAiSpendCap -t {TenantId} -c <EUR> -ks CentralKey -r <reason>' " +
                "as platform admin{FurtherRemedy}", subject, tenantId, furtherRemedy);
            throw;
        }

        switch (result.Outcome)
        {
            case StarterSpendCapResultDto.Created:
                logger.LogInformation("Default LLM cap of tenant '{TenantId}' set: {KeySource}, {Cap} EUR per month",
                    tenantId, result.KeySource, result.CapMonthlyEur);
                break;
            case StarterSpendCapResultDto.Exists:
                logger.LogInformation(
                    "Tenant '{TenantId}' already has an LLM cap ({KeySource}, {Cap} EUR per month); it was not changed",
                    tenantId, result.KeySource, result.CapMonthlyEur);
                break;
            default:
                logger.LogWarning("No default LLM cap set for tenant '{TenantId}': {Message}", tenantId, result.Message);
                break;
        }
    }
}
