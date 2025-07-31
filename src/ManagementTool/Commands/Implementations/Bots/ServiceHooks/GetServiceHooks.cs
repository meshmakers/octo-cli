using GraphQL;
using GraphQlDtos;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Bots.ServiceHooks;

internal class GetServiceHooks : ServiceClientOctoCommand<ITenantClient>
{
    private readonly IConsoleService _consoleService;
    private readonly ITenantClient _tenantClient;

    public GetServiceHooks(ILogger<GetServiceHooks> logger,
        IConsoleService consoleService,
        IOptions<OctoToolOptions> options, ITenantClient tenantClient, IAuthenticationService authenticationService)
        : base(logger, Constants.BotServicesGroup, "GetServiceHooks", "Gets service hooks.", options, tenantClient,
            authenticationService)
    {
        _consoleService = consoleService;
        _tenantClient = tenantClient;
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Getting service hooks from \'{TenantClientServiceUri}\'", _tenantClient.ServiceUri);

        var getQuery = new GraphQLRequest
        {
            Query = GraphQlConstants.GetServiceHook
        };

        var getResult = await _tenantClient.SendQueryAsync<RtServiceHookDto>(getQuery);
        if (getResult?.Items == null || !getResult.Items.Any())
        {
            Logger.LogInformation("No service hooks has been returned");
            return;
        }

        var resultString = JsonConvert.SerializeObject(getResult?.Items, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }
}