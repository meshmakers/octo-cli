using GraphQL;
using GraphQlDtos;
using GraphQlDtos.DataTransferObjects.System.Bot.v1;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.FixupScripts;

internal class CreateFixupScript : ServiceClientOctoCommand<ITenantClient>
{
    private readonly IArgument _fileArg;
    private readonly IArgument _orderNumber;
    private readonly IArgument _name;
    private readonly IArgument _enabled;

    public CreateFixupScript(ILogger<CreateFixupScript> logger, IOptions<OctoToolOptions> options,
        ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "CreateFixupScript", "Creates a fixup script", options, tenantClient,
            authenticationService)
    {
        _enabled = CommandArgumentValue.AddArgument("e", "enabled",
            ["True if the script should be enabled, otherwise false"], true,
            1);
        _name = CommandArgumentValue.AddArgument("n", "name",
            ["Name of the script"], true,
            1);
        _fileArg = CommandArgumentValue.AddArgument("f", "file", ["File to import"], true, 1);

        _orderNumber = CommandArgumentValue.AddArgument("o", "orderNumber",
            ["Order number the script is executed."], true,
            1);
    }

    public override async Task Execute()
    {
        var filePath = CommandArgumentValue.GetArgumentScalarValue<string>(_fileArg).ToLower();

        Logger.LogInformation(
            "Creating fixup script at \'{ValueAssetServiceUrl}\' for tenant \'{ValueTenantId}\'",
            Options.Value.AssetServiceUrl, Options.Value.TenantId);

        if (string.IsNullOrEmpty(Options.Value.TenantId))
        {
            throw ToolException.NoTenantIdConfigured();
        }

        if (!File.Exists(filePath))
        {
            throw ToolException.FilePathDoesNotExist(filePath);
        }

        var createFixup = new RtFixupDto
        {
            Enabled =  CommandArgumentValue.GetArgumentScalarValue<bool>(_enabled),
            Name = CommandArgumentValue.GetArgumentScalarValue<string>(_name),
            Order = CommandArgumentValue.GetArgumentScalarValue<int>(_orderNumber),
            Script = await File.ReadAllTextAsync(filePath)
        };

        var query = new GraphQLRequest
        {
            Query = GraphQlConstants.CreateFixupScript,
            Variables = new { entity = createFixup }
        };

        var dto = await ServiceClient.SendMutationAsync<RtFixupDto>(query);
        Logger.LogInformation("Fixup script created with id \'{Id}\'", dto.RtId);
    }
}