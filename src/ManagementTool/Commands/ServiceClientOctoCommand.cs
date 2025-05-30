using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands;

public abstract class ServiceClientOctoCommand<TServiceClientType>(
    ILogger<ServiceClientOctoCommand<TServiceClientType>> logger,
    string commandGroup,
    string commandValue,
    string commandDescription,
    IOptions<OctoToolOptions> options,
    TServiceClientType serviceClient,
    IAuthenticationService authenticationService)
    : Command<OctoToolOptions>(logger, commandGroup, commandValue, commandDescription, options)
    where TServiceClientType : IServiceClient
{
    protected TServiceClientType ServiceClient { get; } = serviceClient;

    public override async Task PreValidate()
    {
        logger.LogInformation("Service URI: {ServiceClientServiceUri}", ServiceClient.ServiceUri);
        logger.LogInformation("Default Tenant: {TenantId}", Options.Value.TenantId);

        await authenticationService.EnsureAuthenticated(ServiceClient.AccessToken);
    }
}
