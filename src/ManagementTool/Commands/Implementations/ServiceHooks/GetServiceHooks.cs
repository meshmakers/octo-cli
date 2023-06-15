using System.Linq;
using System.Threading.Tasks;
using GraphQL;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Common.Shared.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.ServiceHooks;

internal class GetServiceHooks : ServiceClientOctoCommand<ITenantClient>
{
    private readonly IConsoleService _consoleService;
    private readonly ITenantClient _tenantClient;

    public GetServiceHooks(ILogger<GetServiceHooks> logger,
        IConsoleService consoleService,
        IOptions<OctoToolOptions> options, ITenantClient tenantClient, IAuthenticationService authenticationService)
        : base(logger, "GetServiceHooks", "Gets service hooks.", options, tenantClient, authenticationService)
    {
        _consoleService = consoleService;
        _tenantClient = tenantClient;
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Getting service hooks from \'{TenantClientServiceUri}\'", _tenantClient.ServiceUri);

        var getQuery = new GraphQLRequest
        {
            Query = GraphQl.GetServiceHook
        };

        var getResult = await _tenantClient.SendQueryAsync<RtServiceHookDto>(getQuery);
        if (!getResult?.Items.Any() ?? false)
        {
            Logger.LogInformation("No service hooks has been returned");
            return;
        }

        var resultString = JsonConvert.SerializeObject(getResult?.Items, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }
}
