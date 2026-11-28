using GraphQL;
using GraphQlDtos;
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
///     Prints the roles a client hands to its mirrors (<c>Client.MirrorRoleNames</c> of the original, System.Identity
///     3.9.0), read with a GraphQL query of the context's tenant. Read-only; names only, never a secret.
/// </summary>
internal class GetClientMirrorRoles : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly ITenantClient _tenantClient;
    private readonly IAuthenticationService _authenticationService;
    private readonly IArgument _clientIdArg;
    private readonly IArgument _jsonArg;

    public GetClientMirrorRoles(ILogger<GetClientMirrorRoles> logger, IOptions<OctoToolOptions> options,
        IConsoleService consoleService, IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "GetClientMirrorRoles",
            "Lists the roles a client hands to its mirrors in every child tenant.", options, identityServicesClient,
            authenticationService)
    {
        _consoleService = consoleService;
        _tenantClient = tenantClient;
        _authenticationService = authenticationService;
        _clientIdArg = CommandArgumentValue.AddArgument("id", "clientId",
            ["The ClientId of the original client"], true, 1);
        _jsonArg = CommandArgumentValue.AddArgument("j", "json", ["Output as JSON"], false, 0);
    }

    public override async Task Execute()
    {
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientIdArg);
        Logger.LogInformation("Getting the mirror roles of client '{ClientId}' from '{TenantServiceUri}'", clientId,
            _tenantClient.ServiceUri);

        await _authenticationService.EnsureAuthenticated(_tenantClient.AccessToken);
        QlItemsContainer<RtClientDto>? page;
        try
        {
            page = await _tenantClient.SendQueryAsync<RtClientDto>(new GraphQLRequest
            {
                Query = GraphQlConstants.GetClientMirrorRoleNamesByClientId,
                OperationName = "getClientMirrorRoleNamesByClientId",
                Variables = new { clientId }
            });
        }
        catch (QlQueryErrorException ex) when (IsSchemaValidationError(ex))
        {
            // A tenant whose System.Identity is older than 3.9.0 has no mirrorRoleNames field: name the version, as
            // SetClientMirrorRoles does, instead of the raw GraphQL validation text.
            throw new ServiceClientException(
                $"The service at '{_tenantClient.ServiceUri}' does not know Client.MirrorRoleNames. It needs identity services " +
                $"with System.Identity {IdentityMethodCall.MirrorRolesSystemIdentityVersion} or newer (the mirror role list).");
        }

        var client = page?.Items?.FirstOrDefault(c => string.Equals(c.ClientId, clientId, StringComparison.Ordinal))
                     ?? throw new ToolException($"Client '{clientId}' was not found in tenant '{Options.Value.TenantId}'.");

        var roleNames = (client.MirrorRoleNames ?? []).Where(n => !string.IsNullOrWhiteSpace(n))
            .Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (CommandArgumentValue.IsArgumentUsed(_jsonArg))
        {
            // PascalCase like GetClient and GetClientMirrors.
            _consoleService.WriteLine(JsonConvert.SerializeObject(new { ClientId = clientId, MirrorRoleNames = roleNames },
                Formatting.Indented));
            return;
        }

        _consoleService.WriteLine("Client | Mirror roles");
        _consoleService.WriteLine($"{clientId} | {(roleNames.Count == 0 ? "-" : string.Join(",", roleNames))}");
    }

    /// <summary>A GraphQL validation error (every error carries the spec section <c>extensions.number</c>).</summary>
    private static bool IsSchemaValidationError(QlQueryErrorException exception) =>
        exception.Errors is { Count: > 0 } errors && errors.All(e => e.Extensions != null && e.Extensions.ContainsKey("number"));
}
