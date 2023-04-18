using System.Threading.Tasks;
using Meshmakers.Octo.Frontend.Client;
using Meshmakers.Octo.Frontend.Client.Authentication;

namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

public interface IAuthenticationService
{
    Task EnsureAuthenticated(IServiceClientAccessToken serviceClientAccessToken);

    void SaveAuthenticationData(AuthenticationData authenticationData);
}
