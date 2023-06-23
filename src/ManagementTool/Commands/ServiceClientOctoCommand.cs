using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands;

public abstract class ServiceClientOctoCommand<TServiceClientType> : Command<OctoToolOptions>
    where TServiceClientType : IServiceClient
{
    private readonly IAuthenticationService _authenticationService;


    protected ServiceClientOctoCommand(ILogger<ServiceClientOctoCommand<TServiceClientType>> logger,
        string commandValue, string commandDescription, IOptions<OctoToolOptions> options,
        TServiceClientType serviceClient, IAuthenticationService authenticationService)
        : base(logger, commandValue, commandDescription, options)
    {
        _authenticationService = authenticationService;
        ServiceClient = serviceClient;
    }

    protected TServiceClientType ServiceClient { get; }

    public override async Task PreValidate()
    {
        await _authenticationService.EnsureAuthenticated(ServiceClient.AccessToken);
    }
}
