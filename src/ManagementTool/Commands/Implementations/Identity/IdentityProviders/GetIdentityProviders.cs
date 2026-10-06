using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.IdentityProviders;

internal class GetIdentityProviders : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConsoleService _consoleService;

    public GetIdentityProviders(ILogger<GetIdentityProviders> logger, IOptions<OctoToolOptions> options,
        IConsoleService consoleService,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "GetIdentityProviders", "Gets all identity providers.", options,
            identityServicesClient,
            authenticationService)
    {
        _consoleService = consoleService;
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Getting identity providers from \'{ServiceClientServiceUri}\'",
            ServiceClient.ServiceUri);

        var result = (await ServiceClient.GetIdentityProviders()).ToList();
        if (!result.Any())
        {
            Logger.LogInformation("No identity providers has been returned");
            return;
        }

        ScrubClientSecrets(result);
        var resultString = JsonConvert.SerializeObject(result, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }

    /// <summary>
    ///     Defense in depth (AB#5543): the identity service never returns a client secret (write-only,
    ///     <c>clientSecretIsSet</c> instead), but an older service version did. The output often ends up in
    ///     terminals, CI logs and AI transcripts, so any client secret received is dropped before printing.
    /// </summary>
    internal static void ScrubClientSecrets(IEnumerable<IdentityProviderDto> providers)
    {
        foreach (var provider in providers)
        {
            switch (provider)
            {
                case GoogleIdentityProviderDto google:
                    google.ClientSecret = null;
                    break;
                case MicrosoftIdentityProviderDto microsoft:
                    microsoft.ClientSecret = null;
                    break;
                case FacebookIdentityProviderDto facebook:
                    facebook.ClientSecret = null;
                    break;
                case AzureEntraIdProviderDto azure:
                    azure.ClientSecret = null;
                    break;
            }
        }
    }
}