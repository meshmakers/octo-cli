using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.FixupScripts;

internal class RunFixupScripts(
    ILogger<RunFixupScripts> logger,
    IOptions<OctoToolOptions> options,
    IBotServicesClient botServicesClient,
    IAuthenticationService authenticationService)
    : JobWithWaitOctoCommand(logger, Constants.BotServicesGroup,
        "RunFixupScripts", "Run fixup scripts", options, botServicesClient,
        authenticationService)
{
    public override async Task Execute()
    {
        Logger.LogInformation(
            "Run fixup scripts at \'{ValueAssetServiceUrl}\' for tenant \'{ValueTenantId}\'",
            Options.Value.AssetServiceUrl, Options.Value.TenantId);

        if (string.IsNullOrEmpty(Options.Value.TenantId))
        {
            throw ToolException.NoTenantIdConfigured();
        }


        var response = await ServiceClient.StartRunFixupScript(Options.Value.TenantId);
        Logger.LogInformation("Run fixup scripts with job id \'{Id}\' has been started", response.JobId);
        await WaitForJob(response.JobId);
        Logger.LogInformation("Fixup script created");
    }
}