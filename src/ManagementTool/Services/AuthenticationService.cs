using System.Threading.Tasks;
using Meshmakers.Common.Configuration;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Options;
using NLog;

namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

public class AuthenticationService : IAuthenticationService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly IOptions<OctoToolAuthenticationOptions> _authenticationOptions;
    private readonly IAuthenticatorClient _authenticatorClient;
    private readonly IConfigWriter _configWriter;

    public AuthenticationService(IOptions<OctoToolAuthenticationOptions> authenticationOptions,
        IAuthenticatorClient authenticatorClient, IConfigWriter configWriter)
    {
        _authenticationOptions = authenticationOptions;
        _authenticatorClient = authenticatorClient;
        _configWriter = configWriter;
    }


    public async Task EnsureAuthenticated(IServiceClientAccessToken serviceClientAccessToken)
    {
        if (string.IsNullOrEmpty(_authenticationOptions.Value.RefreshToken) ||
            string.IsNullOrEmpty(_authenticationOptions.Value.AccessToken))
        {
            Logger.Info("No credential data available.");
            return;
        }

        var ensureAuthenticationData = await
            _authenticatorClient.EnsureAuthenticatedAsync(_authenticationOptions.Value.RefreshToken,
                _authenticationOptions.Value.AccessToken);

        if (ensureAuthenticationData.IsRefreshDone)
        {
            SaveAuthenticationData(ensureAuthenticationData.RefreshedAuthenticationData);
            serviceClientAccessToken.AccessToken = ensureAuthenticationData.RefreshedAuthenticationData.AccessToken;
            Logger.Info("Credential data has been refreshed.");
        }
        else
        {
            serviceClientAccessToken.AccessToken = _authenticationOptions.Value.AccessToken;
        }
    }

    public void SaveAuthenticationData(AuthenticationData authenticationData)
    {
        _authenticationOptions.Value.AccessToken = authenticationData.AccessToken;
        _authenticationOptions.Value.RefreshToken = authenticationData.RefreshToken;
        _authenticationOptions.Value.AccessTokenExpiresAt = authenticationData.ExpiresAt;
        _configWriter.WriteSettings(Constants.OctoToolUserFolderName);
    }
}
