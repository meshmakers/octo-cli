using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands;

internal abstract class JobOctoCommand(
    ILogger<JobOctoCommand> logger,
    string commandGroup,
    string commandValue,
    string commandDescription,
    IOptions<OctoToolOptions> options,
    IBotServicesClient botServiceClient,
    IAuthenticationService authenticationService)
    : Command<OctoToolOptions>(logger, commandGroup, commandValue, commandDescription, options)
{
    protected IBotServicesClient ServiceClient { get; } = botServiceClient;

    public override async Task PreValidate()
    {
        Logger.LogInformation("Service URI: {ServiceClientServiceUri}", ServiceClient.ServiceUri);
        Logger.LogInformation("Default Tenant: {TenantId}", Options.Value.TenantId);

        await authenticationService.EnsureAuthenticated(ServiceClient.AccessToken);
    }

    protected async Task DownloadJobResultAsync(string tenantId, string id, string filePath)
    {
        Logger.LogInformation("[{TenantId}] Downloading file of job \'{Id}\'", tenantId, id);

        await ServiceClient.DownloadDumpToFileAsync(tenantId, id, filePath,
            totalBytes =>
            {
                Logger.LogInformation("[{TenantId}] Downloaded {TotalBytes} bytes...", tenantId, totalBytes);
            });

        Logger.LogInformation("[{TenantId}] File downloaded at \'{FilePath}\'", tenantId, filePath);
    }

    protected virtual async Task WaitForJob(string id)
    {
        Logger.LogInformation("Waiting for job \'{Id}\' to finish", id);

        JobDto? lastJobDto = null;
        while (true)
        {
            var jobDto = await ServiceClient.GetImportJobStatus(id);
            if (jobDto.Status == "Succeeded")
            {
                Logger.LogInformation("Job id \'{Id}\' has completed at \'{LocalTime}\'", id,
                    jobDto.StateChangedAt?.ToLocalTime());
                break;
            }

            if (jobDto.Status == "Failed")
            {
                throw ToolException.JobFailed(id, jobDto.StateChangedAt?.ToLocalTime(), jobDto.ErrorMessage);
            }

            if (jobDto.Status == "Deleted")
            {
                throw ToolException.JobDeleted(id, jobDto.StateChangedAt?.ToLocalTime(), jobDto.ErrorMessage);
            }

            if (lastJobDto == null || lastJobDto.StateChangedAt != jobDto.StateChangedAt ||
                lastJobDto.Status != jobDto.Status)
            {
                Logger.LogInformation("Job \'{Id}\' has status \'{JobDtoStatus}\', changed at \'{LocalTime}\'", id,
                    jobDto.Status, jobDto.StateChangedAt?.ToLocalTime());
            }

            lastJobDto = jobDto;
            Thread.Sleep(2000);
        }
    }
}
