using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GraphQL;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Common.Shared.DataTransferObjects;
using Meshmakers.Octo.Frontend.Client.Tenants;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.ServiceHooks;

internal class UpdateServiceHook : ServiceClientOctoCommand<ITenantClient>
{
    private readonly IArgument _ckIdArg;
    private readonly IArgument _filterArg;
    private readonly IArgument _isEnabledArg;
    private readonly IArgument _nameArg;
    private readonly IArgument _serviceHookActionArg;
    private readonly IArgument _serviceHookApiKeyArg;
    private readonly IArgument _serviceHookBaseUriArg;
    private readonly IArgument _serviceHookIdArg;
    private readonly ITenantClient _tenantClient;


    public UpdateServiceHook(ILogger<UpdateServiceHook> logger, IOptions<OctoToolOptions> options,
        ITenantClient tenantClient, IAuthenticationService authenticationService)
        : base(logger, "UpdateServiceHook", "Updates a service hook", options, tenantClient, authenticationService)
    {
        _tenantClient = tenantClient;

        _isEnabledArg = CommandArgumentValue.AddArgument("e", "enabled", new[] { "Enabled state of service hook" },
            false, 1);
        _nameArg = CommandArgumentValue.AddArgument("n", "name", new[] { "Display name of service hook" },
            false, 1);
        _serviceHookIdArg = CommandArgumentValue.AddArgument("id", "serviceHookId",
            new[] { "The id of the service hook" },
            true, 1);
        _ckIdArg = CommandArgumentValue.AddArgument("ck", "ckId",
            new[] { "The construction kit id key the service hook is applied to" },
            false, 1);
        _filterArg = CommandArgumentValue.AddArgument("f", "filter", new[]
            {
                "Filter arguments in form \"'{AttributeName}' {Operator} '{Value}'\"",
                "Sample: \"'State' Equals '2'\"",
                "Attribute name must be a valid argument of the defined entity",
                $"Possible operators: {string.Join(", ", Enum.GetNames(typeof(FieldFilterOperatorDto)))} ",
                "Value must be a string, integer, double, boolean, DateTime"
            },
            false, 1, true);

        _serviceHookBaseUriArg = CommandArgumentValue.AddArgument("u", "uri",
            new[] { "The base uri of the service hook" },
            false, 1);

        _serviceHookActionArg = CommandArgumentValue.AddArgument("a", "action",
            new[] { "The action uri of the service hook" },
            false, 1);

        _serviceHookApiKeyArg = CommandArgumentValue.AddArgument("k", "apiKey",
            new[] { "An optional api key for the service hook, transferred in HTTP header" },
            false, 1);
    }


    public override async Task Execute()
    {
        var serviceHookId = CommandArgumentValue.GetArgumentScalarValue<string>(_serviceHookIdArg);
        var isEnabled = CommandArgumentValue.GetArgumentScalarValueOrDefault<bool>(_isEnabledArg);
        var name = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_nameArg);
        var serviceHookBaseUri = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_serviceHookBaseUriArg);
        var serviceHookAction = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_serviceHookActionArg);
        var serviceHookApiKey = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_serviceHookApiKeyArg);
        var ckId = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_ckIdArg);

        Logger.LogInformation(
            $"Update service hook '{serviceHookId}' at '{_tenantClient.ServiceUri}'");

        var fieldFilters = new List<FieldFilterDto>();
        if (CommandArgumentValue.IsArgumentUsed(_filterArg))
        {
            var filterArgData = CommandArgumentValue.GetArgumentValue(_filterArg);
            foreach (var filterArg in filterArgData.Values)
            {
                var terms = filterArg.Split(" ");
                if (terms.Length != 3)
                {
                    throw new InvalidOperationException(
                        $"Filter term '{filterArg}' is invalid. Three terms needed.");
                }

                var attribute = terms[0].Trim('\'');
                if (!Enum.TryParse(terms[1], true, out FieldFilterOperatorDto operatorDto))
                {
                    throw new InvalidOperationException($"Operator '{terms[1]}' of term '{filterArg}' is invalid.");
                }

                var comparisionValue = terms[2].Trim('\'');

                fieldFilters.Add(new FieldFilterDto
                    { AttributeName = attribute, Operator = operatorDto, ComparisonValue = comparisionValue });
            }
        }

        var getQuery = new GraphQLRequest
        {
            Query = GraphQl.GetServiceHookDetails,
            Variables = new
            {
                rtId = serviceHookId
            }
        };

        var getResult = await _tenantClient.SendQueryAsync<RtServiceHookDto>(getQuery);
        if (!getResult.Items.Any())
        {
            throw new InvalidOperationException(
                $"Service Hook with ID '{serviceHookId}' does not exist.");
        }

        var serviceHookDto = getResult.Items.First();

        var updateServiceHook = new ServiceHookMutationDto
        {
            Enabled = isEnabled,
            Name = name,
            QueryCkId = ckId,
            FieldFilter = JsonConvert.SerializeObject(fieldFilters),
            ServiceHookBaseUri = serviceHookBaseUri,
            ServiceHookAction = serviceHookAction,
            ServiceHookApiKey = serviceHookApiKey
        };

        var updateQuery = new GraphQLRequest
        {
            Query = GraphQl.UpdateServiceHook,
            Variables = new
            {
                entities = new[]
                {
                    new MutationDto<ServiceHookMutationDto>
                    {
                        RtId = serviceHookDto.RtId,
                        Item = updateServiceHook
                    }
                }
            }
        };

        var result = await _tenantClient.SendMutationAsync<IEnumerable<RtServiceHookDto>>(updateQuery);

        Logger.LogInformation($"Service hook '{serviceHookId}' updated (ID '{result.First().RtId}').");
    }
}
