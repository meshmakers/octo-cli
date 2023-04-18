using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Common.Shared.DataTransferObjects;
using Meshmakers.Octo.Frontend.Client.System;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands;

internal abstract class JobOctoCommand : Command<OctoToolOptions>
{
    private readonly IAuthenticationService _authenticationService;

    protected JobOctoCommand(ILogger<JobOctoCommand> logger, string commandValue, string commandDescription,
        IOptions<OctoToolOptions> options,
        IBotServicesClient botServicesClient, IAuthenticationService authenticationService)
        : base(logger, commandValue, commandDescription, options)
    {
        ServicesClient = botServicesClient;
        _authenticationService = authenticationService;
    }

    protected IBotServicesClient ServicesClient { get; }

    public override async Task PreValidate()
    {
        await _authenticationService.EnsureAuthenticated(ServicesClient.AccessToken);
        ServicesClient.Initialize();
    }

    protected async Task DownloadJobResultAsync(string id, string filePath)
    {
        Logger.LogInformation("Downloading file of job \'{Id}\'", id);

        var responseContent = await ServicesClient.DownloadExportRtResultAsync(id);

        await File.WriteAllBytesAsync(filePath, responseContent);
        Logger.LogInformation("File downloaded at \'{FilePath}\'", filePath);
    }

    protected virtual async Task WaitForJob(string id)
    {
        Logger.LogInformation("Waiting for job \'{Id}\' to finish", id);

        JobDto? lastJobDto = null;
        while (true)
        {
            var jobDto = await ServicesClient.GetImportJobStatus(id);
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
