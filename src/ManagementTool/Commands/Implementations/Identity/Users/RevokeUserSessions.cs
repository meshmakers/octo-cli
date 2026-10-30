using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Users;

/// <summary>
///     AB#7899: revokes all sessions and tokens of a user in the tenant of the context (child tenants via <c>-ttid</c>).
/// </summary>
internal class RevokeUserSessions : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _nameArg;
    private readonly IArgument _yesArg;

    public RevokeUserSessions(ILogger<RevokeUserSessions> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.IdentityServicesGroup, "RevokeUserSessions",
            "Revokes all sessions and tokens of a user (refresh tokens, authorizations, browser sessions; rotates the security stamp). Issued access tokens stay valid until they expire.",
            options, identityServicesClient, authenticationService)
    {
        _confirmationService = confirmationService;
        _nameArg = CommandArgumentValue.AddArgument("un", "userName", ["User name"], true, 1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_nameArg, "john.doe")],
                    description: "Sign a user out everywhere in the tenant of the context")
            ]
        );

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg);

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm(
                $"Revoke all sessions and tokens of user '{name}'? The user has to sign in again."))
        {
            throw ToolException.OperationCancelledByUser();
        }

        var result = await ServiceClient.RevokeUserSessions(name);

        Logger.LogInformation(
            "Sessions of user '{UserName}' revoked: {Tokens} token(s), {Authorizations} authorization(s), {Sessions} browser session(s), security stamp rotated: {StampRotated}",
            result.UserName, result.RevokedTokens, result.RevokedAuthorizations, result.RemovedSessions,
            result.SecurityStampRotated);
    }
}
