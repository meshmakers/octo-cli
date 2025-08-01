using GraphQL;
using GraphQlDtos;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Bots.ServiceHooks;

internal class CreateServiceHook : ServiceClientOctoCommand<ITenantClient>
{
    private readonly IArgument _ckIdArg;
    private readonly IArgument _filterArg;
    private readonly IArgument _isEnabledArg;
    private readonly IArgument _nameArg;
    private readonly IArgument _serviceHookActionArg;
    private readonly IArgument _serviceHookApiKeyArg;
    private readonly IArgument _serviceHookBaseUriArg;

    public CreateServiceHook(ILogger<CreateServiceHook> logger, IOptions<OctoToolOptions> options,
        ITenantClient tenantClient, IAuthenticationService authenticationService)
        : base(logger, Constants.BotServicesGroup, "CreateServiceHook", "Create a new service hook", options,
            tenantClient, authenticationService)
    {
        _isEnabledArg = CommandArgumentValue.AddArgument("e", "enabled", ["Enabled state of service hook"],
            true, 1);
        _nameArg = CommandArgumentValue.AddArgument("n", "name", ["Display name of service hook"],
            true, 1);
        _ckIdArg = CommandArgumentValue.AddArgument("ck", "ckId",
            ["The construction kit id key the service hook is applied to"],
            true, 1);
        _filterArg = CommandArgumentValue.AddArgument("f", "filter", [
                "Filter arguments in form \"'{AttributeName}' {Operator} '{Value}'\"",
                "Sample: \"'State' Equals '2'\"",
                "Attribute name must be a valid argument of the defined entity",
                $"Possible operators: {string.Join(", ", Enum.GetNames(typeof(FieldFilterOperatorDto)))} ",
                "Value must be a string, integer, double, boolean, DateTime"
            ],
            true, 1, true);
        _serviceHookBaseUriArg = CommandArgumentValue.AddArgument("u", "uri",
            ["The base uri of the service hook"],
            false, 1);

        _serviceHookActionArg = CommandArgumentValue.AddArgument("a", "action",
            ["The action uri of the service hook"],
            false, 1);

        _serviceHookApiKeyArg = CommandArgumentValue.AddArgument("k", "apiKey",
            ["An optional api key for the service hook, transferred in HTTP header"],
            false, 1);
    }


    public override async Task Execute()
    {
        var isEnabled = CommandArgumentValue.GetArgumentScalarValue<bool>(_isEnabledArg);
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg);
        var serviceHookBaseUri = CommandArgumentValue.GetArgumentScalarValue<string>(_serviceHookBaseUriArg);
        var serviceHookAction = CommandArgumentValue.GetArgumentScalarValue<string>(_serviceHookActionArg);
        var serviceHookApiKey = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_serviceHookApiKeyArg);
        var ckId = CommandArgumentValue.GetArgumentScalarValue<string>(_ckIdArg);

        var filterArgData = CommandArgumentValue.GetArgumentValue(_filterArg);

        Logger.LogInformation("Creating service hook for entity \'{CkId}\' at \'{ServiceClientServiceUri}\'", ckId,
            ServiceClient.ServiceUri);

        var fieldFilters = new List<FieldFilterDto>();
        foreach (var filterArg in filterArgData.Values)
        {
            var terms = filterArg.Split(" ");
            if (terms.Length != 3)
            {
                throw ToolException.InvalidFilterTerm(filterArg);
            }

            var attribute = terms[0].Trim('\'');
            if (!Enum.TryParse(terms[1], true, out FieldFilterOperatorDto operatorDto))
            {
                throw ToolException.InvalidFilterOperator(filterArg, terms[1]);
            }

            var comparisionValue = terms[2].Trim('\'');

            fieldFilters.Add(new FieldFilterDto
                { AttributePath = attribute, Operator = operatorDto, ComparisonValue = comparisionValue });
        }

        var createServiceHookDto = new ServiceHookMutationDto
        {
            Enabled = isEnabled,
            Name = name,
            QueryCkTypeId = ckId,
            FieldFilter = JsonConvert.SerializeObject(fieldFilters),
            ServiceHookBaseUri = serviceHookBaseUri,
            ServiceHookAction = serviceHookAction,
            ServiceHookApiKey = serviceHookApiKey
        };

        var query = new GraphQLRequest
        {
            Query = GraphQlConstants.CreateServiceHook,
            Variables = new { entities = new[] { createServiceHookDto } }
        };

        var result = await ServiceClient.SendMutationAsync<IEnumerable<RtServiceHookDto>>(query);

        Logger.LogInformation("Service hook \'{Name}\' added (ID \'{RtId}\')", name, result.First().RtId);
    }
}