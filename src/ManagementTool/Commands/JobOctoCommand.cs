using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands;

internal abstract class JobOctoCommand : Command<OctoToolOptions>
{
    private readonly IAuthenticationService _authenticationService;

    protected JobOctoCommand(ILogger<JobOctoCommand> logger, string commandValue, string commandDescription,
        IOptions<OctoToolOptions> options,
        IBotServicesClient botServiceClient, IAuthenticationService authenticationService)
        : base(logger, commandValue, commandDescription, options)
    {
        ServiceClient = botServiceClient;
        _authenticationService = authenticationService;
    }

    protected IBotServicesClient ServiceClient { get; }

    public override async Task PreValidate()
    {
        await _authenticationService.EnsureAuthenticated(ServiceClient.AccessToken);
    }

    protected async Task DownloadJobResultAsync(string tenantId, string id, string filePath)
    {
        Logger.LogInformation("[{TenantId}] Downloading file of job \'{Id}\'", tenantId, id);

        var responseContent = await ServiceClient.DownloadExportRtResultAsync(tenantId, id);

        await File.WriteAllBytesAsync(filePath, responseContent);
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
                Logger.LogInformation("Job id \'{Id}\' has failed at \'{LocalTime}\'. See server logs for more details",
                    id, jobDto.StateChangedAt?.ToLocalTime());
                break;
            }

            if (jobDto.Status == "Deleted")
            {
                Logger.LogInformation("Job id \'{Id}\' has failed at \'{LocalTime}\'. See server logs for more details",
                    id, jobDto.StateChangedAt?.ToLocalTime());
                break;
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
