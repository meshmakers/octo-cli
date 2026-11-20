using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.ApiSecrets;

internal class CreateApiSecretClient : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IdentityMethodCall _methods;
    private readonly IArgument _clientIdArg;
    private readonly IConsoleService _consoleService;
    private readonly IArgument _descriptionArg;
    private readonly IArgument _expirationArg;

    public CreateApiSecretClient(ILogger<CreateApiSecretClient> logger, IOptions<OctoToolOptions> options,
        IConsoleService consoleService,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "CreateApiSecretClient", "Adds new API secret for a client.",
            options,
            identityServicesClient, authenticationService)
    {
        _methods = new IdentityMethodCall(tenantClient, authenticationService, logger);
        _consoleService = consoleService;

        _clientIdArg = CommandArgumentValue.AddArgument("cid", "clientId", ["ID of client"],
            true,
            1);
        _expirationArg =
            CommandArgumentValue.AddArgument("e", "expirationDate", ["Expiration date of secret"], false, 1);
        _descriptionArg =
            CommandArgumentValue.AddArgument("d", "description", ["Description of API secret"], false, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_clientIdArg, "my-client"),
                    new CodeSampleArgument(_expirationArg, "2025-12-31"),
                    new CodeSampleArgument(_descriptionArg, "Production secret"),
                ],
                    description: "Basic usage"),
            ],
            Notes:
            [
                "Writing API secrets needs the tenant role `SecretManagement`. `TenantManagement` or `UserManagement` still pass while the identity cluster runs with `SecretManagementRoleEnforcement=Warn` or `RoleEnforcement=Warn`; they are refused only when `SecretManagementRoleEnforcement=Enforce` and `RoleEnforcement` is not `Warn`. A refused call (HTTP 403) prints the message of the identity service, which names the missing role; without a message the generic access-denied text is shown.",
            ]
        );

    public override async Task Execute()
    {
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientIdArg);

        Logger.LogInformation("Creating API secret for client \'{ClientId}\' at \'{ServiceClientServiceUri}\'",
            clientId,
            ServiceClient.ServiceUri);

        var apiSecretDto = new ApiSecretDto
        {
            ExpirationDate = CommandArgumentValue.GetArgumentScalarValueOrDefault<DateTime?>(_expirationArg),
            Description = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_descriptionArg)
        };

        var clearText = await _methods.RunAsync(RtClientMethodClient.AddSecretMethodId,
            async () => await new RtClientMethodClient(_methods.TenantClient).AddSecretAsync(await IdentityMethodCall.ResolveClientRtIdAsync(ServiceClient, clientId),
                apiSecretDto.Description, apiSecretDto.ExpirationDate));
        var resultSecret = IdentityMethodCall.ToNewSecretDto(clearText, apiSecretDto.ExpirationDate,
            apiSecretDto.Description);

        // The hash identifies the secret for later updates and is part of the printed result, but neither
        // it nor the clear text is ever written to the log (AB#6258).
        Logger.LogInformation("API secret for client '{ClientId}' at '{ServiceClientServiceUri}' created",
            clientId, ServiceClient.ServiceUri);
        Logger.LogWarning("The clear-text secret is shown below once only and cannot be retrieved again.");

        var resultString = JsonConvert.SerializeObject(resultSecret, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }
}