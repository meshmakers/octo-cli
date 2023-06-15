using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.LargeBinaries;

internal class CreateLargeBinary : ServiceClientOctoCommand<ITenantClient>
{
    private readonly IArgument _contentTypeArg;
    private readonly IArgument _filePathArg;
    private readonly ITenantClientAccessToken _tenantClientAccessToken;

    public CreateLargeBinary(ILogger<CreateLargeBinary> logger, IOptions<OctoToolOptions> options,
        ITenantClient tenantClient, IAuthenticationService authenticationService,
        ITenantClientAccessToken tenantClientAccessToken)
        : base(logger, "CreateLargeBinary", "Uploads a file and creates a large binary in database", options,
            tenantClient, authenticationService)
    {
        _tenantClientAccessToken = tenantClientAccessToken;

        _filePathArg = CommandArgumentValue.AddArgument("f", "file", new[] { "Path to file that is uploaded." },
            true, 1);
        _contentTypeArg = CommandArgumentValue.AddArgument("ct", "contentType", new[] { "Content type of the file." },
            true, 1);
    }


    public override async Task Execute()
    {
        var filePath = CommandArgumentValue.GetArgumentScalarValue<string>(_filePathArg);
        var contentType = CommandArgumentValue.GetArgumentScalarValue<string>(_contentTypeArg);

        Logger.LogInformation("Uploading large binary \'{FilePath}\' at \'{ServiceClientServiceUri}\'", filePath,
            ServiceClient.ServiceUri);
        if (string.IsNullOrEmpty(Options.Value.TenantId))
        {
            Logger.LogError("No tenant id has been saved in configuration use --config to set a value");
            return;
        }

        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", $"bearer {_tenantClientAccessToken.AccessToken}");

        var content = new MultipartFormDataContent();

        var queryContent = new StringContent(GraphQl.CreateLargeBinary);
        queryContent.Headers.ContentType = new MediaTypeHeaderValue("application/graphql");
        content.Add(queryContent, "query");

        await using var stream = File.Open(filePath, FileMode.Open);

        var streamContent = new StreamContent(stream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(streamContent, "binaryData", Path.GetFileName(filePath));

        using var response =
            await client.PostAsync($"{Options.Value.AssetServiceUrl}tenants/{Options.Value.TenantId}/graphql", content);
        var result = await response.Content.ReadAsStringAsync();
        Console.WriteLine(result);
        Logger.LogInformation("Large binary uploaded (StatusCode \'{StatusCode}\')", response.StatusCode);
    }
}
