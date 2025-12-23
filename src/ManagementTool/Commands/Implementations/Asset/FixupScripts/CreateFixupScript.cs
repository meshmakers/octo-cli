using GraphQL;
using GraphQlDtos;
using GraphQlDtos.DataTransferObjects.System.Bot.v2;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
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
    private readonly IArgument _replaceArg;

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
        _replaceArg = CommandArgumentValue.AddArgument("r", "replace",
            ["When defined, an existing fixup script is replaced."], false, 0);}

    public override async Task Execute()
    {
        var filePath = CommandArgumentValue.GetArgumentScalarValue<string>(_fileArg);

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

        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_name);

        var queryByName =  new GraphQLRequest {
            Query = GraphQlConstants.GetFixupScriptByName,
            Variables = new { name }
        };
        var r = await ServiceClient.SendQueryAsync<RtFixupDto>(queryByName);

        if (CommandArgumentValue.IsArgumentUsed(_replaceArg) && r is { TotalCount: > 0 })
        {
            var dto = r.Items?.FirstOrDefault();
            if (dto != null)
            {
                Logger.LogInformation("Fixup script with name \'{Name}\' already exists, replacing it with new one",
                    name);

                if (dto.IsApplied.GetValueOrDefault())
                {
                    throw ToolException.FixupScriptAlreadyApplied(name);
                }

                var updateMutation = new GraphQLRequest
                {
                    Query = GraphQlConstants.UpdateFixupScript,
                    Variables = new
                    {
                        entities = new[]
                        {
                            new MutationDto<RtFixupMutationDto>
                            {
                                RtId = dto.RtId,
                                Item = new RtFixupMutationDto
                                {
                                    Enabled = CommandArgumentValue.GetArgumentScalarValue<bool>(_enabled),
                                    Name = CommandArgumentValue.GetArgumentScalarValue<string>(_name),
                                    Order = CommandArgumentValue.GetArgumentScalarValue<int>(_orderNumber),
                                    Script = await File.ReadAllTextAsync(filePath)
                                }
                            }
                        }
                    }
                };
                var updateDto = await ServiceClient.SendMutationAsync<RtFixupDto>(updateMutation);
                Logger.LogInformation("Fixup script with id \'{Id}\' updated", updateDto.RtId);
            }
        }
        else if (r is { TotalCount: > 0 })
        {
            throw ToolException.FixupScriptAlreadyExists(name);
        }
        else
        {
            var createFixup = new RtFixupDto
            {
                IsApplied = false,
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
}