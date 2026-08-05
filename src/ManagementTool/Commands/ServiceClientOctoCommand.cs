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

    /// <summary>
    ///     Names the tenant and host this command's service client is scoped to — the parent of any
    ///     child tenant named by an argument such as <c>-tid</c> or <c>-ctid</c>.
    ///     <para>
    ///         Meant for the confirmation prompt of destructive commands that act on a child tenant.
    ///         The child alone does not identify the operation: the parent scope comes from the
    ///         effective context, which <c>--context</c> can change per invocation, and two contexts
    ///         may well carry the same tenant id against different environments. Naming both is what
    ///         makes "yes" an informed answer.
    ///     </para>
    /// </summary>
    /// <remarks>
    ///     Reads <c>ServiceUri</c>, which <c>PreValidate</c> has already built and cached, so a
    ///     missing endpoint or tenant has been reported before any prompt appears.
    /// </remarks>
    protected string ParentScopeDescription =>
        $"parent tenant '{Options.Value.TenantId ?? "<none>"}' " +
        $"at '{ServiceClient.ServiceUri.GetLeftPart(UriPartial.Authority)}'";

    public override async Task PreValidate()
    {
        logger.LogInformation("Service URI: {ServiceClientServiceUri}", ServiceClient.ServiceUri);
        logger.LogInformation("Using tenant: {TenantId}", Options.Value.TenantId);

        await authenticationService.EnsureAuthenticated(ServiceClient.AccessToken);
    }
}
