using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using RestSharp;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.PipelineServiceAccounts;

/// <summary>
///     AB#6542: read-only check which pipeline service accounts (<c>octo-pipeline-sa-*</c>) of the tenant hold the
///     FileManagement role. Exits non-zero when at least one account is missing it, so a rollout runbook can loop
///     over the tenants of a cluster and gate on the result. Nothing is granted and no secret is printed.
/// </summary>
internal class CheckPipelineServiceAccountFileRoles : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConsoleService _consoleService;

    public CheckPipelineServiceAccountFileRoles(ILogger<CheckPipelineServiceAccountFileRoles> logger,
        IConsoleService consoleService,
        IOptions<OctoToolOptions> options, IIdentityServicesClient identityServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "CheckPipelineServiceAccountFileRoles",
            "Reports which pipeline service accounts (octo-pipeline-sa-*) hold FileManagement; exits non-zero when one is missing. Read-only.",
            options, identityServicesClient, authenticationService)
    {
        _consoleService = consoleService;
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Checking pipeline service account file roles at '{ServiceClientServiceUri}'",
            ServiceClient.ServiceUri);

        // The SDK has no client method for the read endpoint yet; call it directly with the command's token.
        using var client = new RestClient(ServiceClient.ServiceUri);
        var request = new RestRequest("pipelineServiceAccountFileRoles");
        request.AddHeader("Authorization", $"bearer {ServiceClient.AccessToken.AccessToken}");
        var response = await client.ExecuteAsync(request);
        if (!response.IsSuccessful || string.IsNullOrEmpty(response.Content))
        {
            throw new ToolException(
                $"Pipeline service account file role check failed: HTTP {(int)response.StatusCode} {response.StatusDescription}");
        }

        var report = JsonConvert.DeserializeObject<Report>(response.Content)
                     ?? throw new ToolException("The identity service returned an empty report.");
        _consoleService.WriteLine(JsonConvert.SerializeObject(report, Formatting.Indented));

        if (report.MissingCount > 0)
        {
            throw new ToolException(
                $"{report.MissingCount} pipeline service account(s) of tenant '{report.TenantId}' lack FileManagement.");
        }
    }

    private sealed class Report
    {
        public string TenantId { get; set; } = "";
        public string Status { get; set; } = "";
        public int MissingCount { get; set; }
        public List<Entry> Accounts { get; set; } = [];
    }

    private sealed class Entry
    {
        public string ClientId { get; set; } = "";
        public string RtId { get; set; } = "";
        public bool HasFileManagement { get; set; }
    }
}
