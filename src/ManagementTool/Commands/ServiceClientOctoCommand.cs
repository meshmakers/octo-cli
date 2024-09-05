using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands;

public abstract class ServiceClientOctoCommand<TServiceClientType>(
    ILogger<ServiceClientOctoCommand<TServiceClientType>> logger,
    string commandValue,
    string commandDescription,
    IOptions<OctoToolOptions> options,
    TServiceClientType serviceClient,
    IAuthenticationService authenticationService)
    : Command<OctoToolOptions>(logger, commandValue, commandDescription, options)
    where TServiceClientType : IServiceClient
{
    protected TServiceClientType ServiceClient { get; } = serviceClient;

    public override async Task PreValidate()
    {
        await authenticationService.EnsureAuthenticated(ServiceClient.AccessToken);
    }
}
