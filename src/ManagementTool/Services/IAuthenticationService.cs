using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;

namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

public interface IAuthenticationService
{
    Task EnsureAuthenticated(IServiceClientAccessToken serviceClientAccessToken);

    void SaveAuthenticationData(AuthenticationData authenticationData);
}
