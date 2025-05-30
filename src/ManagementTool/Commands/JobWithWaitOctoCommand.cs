using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands;

internal abstract class JobWithWaitOctoCommand : JobOctoCommand
{
    private readonly IArgument _waitForJobArg;

    protected JobWithWaitOctoCommand(ILogger<JobWithWaitOctoCommand> logger, string commandGroup, string commandValue,
        string commandDescription, IOptions<OctoToolOptions> options,
        IBotServicesClient botServicesClient, IAuthenticationService authenticationService)
        : base(logger, commandGroup, commandValue, commandDescription, options, botServicesClient, authenticationService)
    {
        _waitForJobArg = CommandArgumentValue.AddArgument("w", "wait",
            ["Wait for a import job to complete"], false, 0);
    }

    protected override async Task WaitForJob(string id)
    {
        if (CommandArgumentValue.IsArgumentUsed(_waitForJobArg))
        {
            await base.WaitForJob(id);
        }
    }
}
