using System.Net;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects.ApiErrors;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.ExternalTenantUserMappings;

/// <summary>
///     AB#8346: finds exactly one user of a source tenant by exact user name or e-mail, with the context's tenant as target
///     tenant (identity AB#8341, <c>POST externalTenantUserMappings/sourceUserLookup</c>). The result feeds
///     <c>CreateExternalTenantUserMapping</c>. The identifier is never written to the log: only the service's answer is
///     printed, and that answer carries no e-mail address.
/// </summary>
internal class LookupSourceTenantUser : ServiceClientOctoCommand<IIdentityServicesClient>
{
    /// <summary>The one message for every negative answer (uniform 404 of the service; never a reason).</summary>
    internal const string NotFoundMessage =
        "No user found that can be granted access (code " + CrossTenantGrantProblemCodes.SourceUserNotFound + ").";

    private readonly IConsoleService _consoleService;
    private readonly IArgument _identifier;
    private readonly IArgument _sourceTenantId;

    public LookupSourceTenantUser(ILogger<LookupSourceTenantUser> logger, IOptions<OctoToolOptions> options,
        IConsoleService consoleService, IIdentityServicesClient identityServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "LookupSourceTenantUser",
            "Finds one user of a source tenant by exact user name or e-mail, to grant that user access to the tenant of the context.",
            options, identityServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _sourceTenantId = CommandArgumentValue.AddArgument("st", "sourceTenantId",
            ["Source tenant ID: a registry ancestor, or a tenant whose allow-list permits the tenant of the context"],
            true, 1);
        _identifier = CommandArgumentValue.AddArgument("id", "identifier",
            ["Exact user name or exact e-mail of the user in the source tenant (case-insensitive, no wildcards)"], true, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments:
                    [
                        new CodeSampleArgument(_sourceTenantId, "meshmakers"),
                        new CodeSampleArgument(_identifier, "alice@meshmakers.io")
                    ],
                    description: "Find a user of a sibling tenant that permits this tenant")
            ],
            Notes:
            [
                "Prints sourceTenantId, userId, userName and displayName (never the e-mail). Pass userId and userName to CreateExternalTenantUserMapping.",
                "Every negative case (tenant absent or not permitting, no exact match, several matches, shadow or locked user) gives the same answer 'No user found that can be granted access' and exit code -3.",
                "Lookups are limited per caller and tenant (default 20 per 10 minutes); over the limit the answer names the wait time (HTTP 429, code SourceUserLookupRateLimited).",
                "Needs UserManagement in the tenant of the context and an identity service with AB#8341."
            ]);

    public override async Task Execute()
    {
        var sourceTenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_sourceTenantId);
        var identifier = CommandArgumentValue.GetArgumentScalarValue<string>(_identifier);

        // The identifier (often an e-mail address) is deliberately not logged.
        Logger.LogInformation("Looking up a user of source tenant '{SourceTenantId}' at '{ServiceClientServiceUri}'",
            sourceTenantId, ServiceClient.ServiceUri);

        var user = await ServiceClient.LookupSourceUser(new SourceUserLookupRequestDto
        {
            SourceTenantId = sourceTenantId,
            Identifier = identifier
        });

        if (user == null)
        {
            throw new ServiceClientResultException(NotFoundMessage, HttpStatusCode.NotFound);
        }

        _consoleService.WriteLine(JsonConvert.SerializeObject(user, Formatting.Indented));
    }
}
