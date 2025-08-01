using GraphQL;
using GraphQlDtos;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Bots.ServiceHooks;

internal class DeleteServiceHook : ServiceClientOctoCommand<ITenantClient>
{
    private readonly IArgument _serviceHookIdArg;
    private readonly ITenantClient _tenantClient;

    public DeleteServiceHook(ILogger<DeleteServiceHook> logger, IOptions<OctoToolOptions> options,
        ITenantClient tenantClient, IAuthenticationService authenticationService)
        : base(logger, Constants.BotServicesGroup, "DeleteServiceHook", "Deletes a service hook", options, tenantClient,
            authenticationService)
    {
        _tenantClient = tenantClient;

        _serviceHookIdArg = CommandArgumentValue.AddArgument("id", "serviceHookId", ["ID of the service hook"],
            true, 1);
    }

    public override async Task Execute()
    {
        var serviceHookId = CommandArgumentValue.GetArgumentScalarValue<string>(_serviceHookIdArg);

        Logger.LogInformation("Deleting service hook \'{ServiceHookId}\' at \'{TenantClientServiceUri}\'",
            serviceHookId, _tenantClient.ServiceUri);

        var getQuery = new GraphQLRequest
        {
            Query = GraphQlConstants.GetServiceHookDetails,
            Variables = new
            {
                rtId = serviceHookId
            }
        };

        var getResult = await _tenantClient.SendQueryAsync<RtServiceHookDto>(getQuery);
        if (getResult?.Items == null || !getResult.Items.Any())
        {
            throw new InvalidOperationException(
                $"Service Hook with ID '{serviceHookId}' does not exist.");
        }

        var serviceHookDto = getResult.Items.First();

        var deleteMutation = new GraphQLRequest
        {
            Query = GraphQlConstants.DeleteServiceHook,
            Variables = new
            {
                entities = new[]
                {
                    new MutationDto
                    {
                        RtId = serviceHookDto.RtId
                    }
                }
            }
        };

        var result = await _tenantClient.SendMutationAsync<bool>(deleteMutation);

        if (result)
        {
            Logger.LogInformation("Service hook \'{ServiceHookId}\' deleted", serviceHookId);
        }
        else
        {
            Logger.LogError("Service hook \'{ServiceHookId}\' delete failed", serviceHookId);
        }
    }
}