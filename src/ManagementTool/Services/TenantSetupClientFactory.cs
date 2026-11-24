using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

/// <summary>
///     Builds the setup client of one tenant: <c>POST {tenant}/v1/setup</c> on the identity service of the context.
///     The SDK client addresses the system route only.
/// </summary>
public interface ITenantSetupClientFactory
{
    /// <summary>The setup client of <paramref name="tenantId" />; its access token is still empty.</summary>
    IIdentityServicesSetupClient Create(string tenantId);
}

/// <inheritdoc />
public sealed class TenantSetupClientFactory(IOptions<OctoToolOptions> options) : ITenantSetupClientFactory
{
    /// <inheritdoc />
    public IIdentityServicesSetupClient Create(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var clientOptions = new IdentityServiceClientOptions
        {
            EndpointUri = options.Value.IdentityServiceUrl,
            TenantId = tenantId
        };
        return new TenantIdentityServicesSetupClient(clientOptions);
    }

    private sealed class TenantIdentityServicesSetupClient(IdentityServiceClientOptions clientOptions)
        : IdentityServicesSetupClient(clientOptions)
    {
        private readonly string _tenantId = clientOptions.TenantId!;

        protected override Uri BuildServiceUri()
        {
            if (string.IsNullOrWhiteSpace(Options.EndpointUri))
            {
                throw new ServiceConfigurationMissingException("Identity services URI is missing.");
            }

            return new Uri(Options.EndpointUri).Append(_tenantId).Append("v1");
        }
    }
}
