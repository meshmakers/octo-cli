using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Users;

/// <summary>
///     AB#7899: revokes all sessions and tokens of every cross-tenant shadow user (<c>xt_*</c>) in the tenant of the
///     context. Dry run unless <c>--apply</c> is given.
/// </summary>
internal class RevokeShadowUserSessions : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _applyArg;
    private readonly IArgument _yesArg;

    public RevokeShadowUserSessions(ILogger<RevokeShadowUserSessions> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.IdentityServicesGroup, "RevokeShadowUserSessions",
            "Revokes all sessions and tokens of the cross-tenant shadow users (xt_*) of the tenant. Dry run (counts only) unless --apply is given.",
            options, identityServicesClient, authenticationService)
    {
        _confirmationService = confirmationService;
        _applyArg = CommandArgumentValue.AddArgument("a", "apply", ["Revoke; without it only the counts are shown"],
            false, 0);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt (with --apply)"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [], description: "Dry run: show how many shadow users, tokens and sessions are affected"),
                new CodeSample(arguments: [new CodeSampleArgument(_applyArg)], description: "Revoke")
            ]
        );

    public override async Task Execute()
    {
        var apply = CommandArgumentValue.IsArgumentUsed(_applyArg);

        if (apply && !CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm(
                "Revoke all sessions and tokens of every cross-tenant shadow user (xt_*) of this tenant?"))
        {
            throw ToolException.OperationCancelledByUser();
        }

        var result = await ServiceClient.RevokeShadowUserSessions(dryRun: !apply);

        Logger.LogInformation(
            "{Mode} tenant '{TenantId}': {Users} shadow user(s), {Tokens} token(s), {Authorizations} authorization(s), {Sessions} browser session(s)",
            result.DryRun ? "Dry run (nothing changed, use --apply to revoke)" : "Revoked in",
            result.TenantId, result.Users, result.Tokens, result.Authorizations, result.Sessions);
        foreach (var userName in result.UserNames)
        {
            Logger.LogInformation("  {UserName}", userName);
        }

        if (result.UserNames.Count < result.Users)
        {
            Logger.LogInformation("  … and {More} more", result.Users - result.UserNames.Count);
        }
    }
}
