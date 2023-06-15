using System.IO;
using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.LargeBinaries;

internal class DownloadLargeBinary : ServiceClientOctoCommand<ITenantClient>
{
    private readonly IArgument _filePathArg;
    private readonly IArgument _idArg;

    public DownloadLargeBinary(ILogger<DownloadLargeBinary> logger, IOptions<OctoToolOptions> options,
        ITenantClient tenantClient, IAuthenticationService authenticationService)
        : base(logger, "DownloadLargeBinary", "Downloads a file and from database", options, tenantClient,
            authenticationService)
    {
        _filePathArg = CommandArgumentValue.AddArgument("f", "file",
            new[] { "Path to the download location of the file." },
            true, 1);
        _idArg = CommandArgumentValue.AddArgument("id", "largeBinaryId",
            new[] { "id of the binary to be downloaded." },
            true, 1);
    }


    public override async Task Execute()
    {
        var filePath = CommandArgumentValue.GetArgumentScalarValue<string>(_filePathArg);
        var id = CommandArgumentValue.GetArgumentScalarValue<string>(_idArg);

        Logger.LogInformation("Downloading large binary \'{Id}\' at \'{ServiceClientServiceUri}\'", id,
            ServiceClient.ServiceUri);
        if (string.IsNullOrEmpty(Options.Value.AssetServiceUrl))
        {
            Logger.LogError("No core service uri has been saved in configuration use --config to set a value");
            return;
        }

        var uri = Options.Value.AssetServiceUrl.EnsureEndsWith("/");

        using var response =
            await ServiceClient.HttpClient.GetAsync(
                $"{uri}system/v1/LargeBinaries?tenantId={Options.Value.TenantId}&largeBinaryId={id}");
        if (response.IsSuccessStatusCode)
        {
            var stream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = File.OpenWrite(filePath);
            await stream.CopyToAsync(fileStream);
            stream.Close();
            fileStream.Close();
            Logger.LogInformation("Large binary downloaded (ID \'{Id}\') to \'{FilePath}\'", id, filePath);
            return;
        }

        Logger.LogError("Service returned code \'{ResponseReasonPhrase}\'", response.ReasonPhrase);
    }
}
