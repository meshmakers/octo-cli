using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services.WorkloadResources;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class GetAdapterCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IAssetServicesClient _assetServicesClient;
    private readonly IConsoleService _consoleService;
    private readonly IArgument _idArg;
    private readonly IArgument _jsonArg;
    private readonly IArgument _showSecretsArg;

    public GetAdapterCommand(ILogger<GetAdapterCommand> logger, IOptions<OctoToolOptions> options,
        IConsoleService consoleService,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService,
        IAssetServicesClient assetServicesClient)
        : base(logger, Constants.CommunicationServicesGroup, "GetAdapter",
            "Gets the configuration for a specific adapter.", options,
            communicationServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _assetServicesClient = assetServicesClient;
        _idArg = CommandArgumentValue.AddArgument("id", "identifier", ["The adapter runtime ID"], true, 1);
        _jsonArg = CommandArgumentValue.AddArgument("j", "json", ["Output as raw JSON"], false);
        _showSecretsArg = CommandArgumentValue.AddArgument("ss", "show-secrets",
            [
                "Print credentials (passwords, secrets, API keys, tokens) of the adapter and pipeline configurations in clear text (AB#6286).",
                "Without this flag they are masked, so the output is safe for logs and transcripts"
            ], false);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_idArg, "69cfa838092b710403248acd")], description: "Basic usage"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_idArg, "69cfa838092b710403248acd"),
                    new CodeSampleArgument(_jsonArg),
                ],
                    description: "Output as compact JSON"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_idArg, "69cfa838092b710403248acd"),
                    new CodeSampleArgument(_showSecretsArg),
                ],
                    description: "Prints passwords and other credentials in clear text (secrets!)"),
            ],
            Notes:
            [
                "- The output carries a 'resources' object with the CPU/memory requests and limits and the LastDeploymentWarning (AB#7429). An unset value reads 'chart default' (null with --json). The object is left out when the tenant's System.Communication model is older than 4.10.0. Set the values with UpdateWorkloadResources.",
                "- Credentials in the adapter configuration and in the pipeline configurations (members named like password, secret, apiKey, accessToken, privateKey, connectionString, ...) are masked by default, because the output of this command ends up in scripts, CI logs and AI-agent transcripts (AB#6286). `--show-secrets` (`-ss`) prints them in clear text; use it only interactively when you really need the value.",
            ]
        );

    public override async Task Execute()
    {
        var id = CommandArgumentValue.GetArgumentScalarValue<string>(_idArg);

        Logger.LogInformation(
            "Getting adapter '{AdapterId}' for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            id, Options.Value.TenantId, ServiceClient.ServiceUri);

        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var result = await ServiceClient.GetAdapterConfigurationAsync(id);

        var output = JToken.FromObject(result, JsonSerializer.CreateDefault());
        if (!CommandArgumentValue.IsArgumentUsed(_showSecretsArg))
        {
            output = SecretMasker.MaskToken(output);
            Logger.LogInformation("Credentials are masked; use --show-secrets to print them in clear text");
        }

        var asJson = CommandArgumentValue.IsArgumentUsed(_jsonArg);
        await AddResourcesAsync(output, id, asJson);

        _consoleService.WriteLine(output.ToString(CommandArgumentValue.IsArgumentUsed(_jsonArg)
            ? Formatting.None
            : Formatting.Indented));
    }

    private async Task AddResourcesAsync(JToken output, string id, bool asJson)
    {
        if (output is not JObject adapter)
        {
            return;
        }

        try
        {
            var tenantId = Options.Value.TenantId!.Trim().ToLowerInvariant();
            var state = await new WorkloadResourcesAccess(_assetServicesClient).FindAsync(tenantId, id);
            if (state == null)
            {
                return;
            }

            const string chartDefault = "chart default";
            JToken Value(string? value) =>
                value != null ? value : asJson ? JValue.CreateNull() : chartDefault;
            var settings = state.Settings;
            adapter["resources"] = new JObject
            {
                ["cpuRequest"] = Value(settings.CpuRequest),
                ["cpuLimit"] = Value(settings.CpuLimit),
                ["memoryRequest"] = Value(settings.MemoryRequest),
                ["memoryLimit"] = Value(settings.MemoryLimit),
                ["lastDeploymentWarning"] = settings.LastDeploymentWarning
            };
        }
        catch (ToolException e)
        {
            // The adapter itself was read; the sizing is an addition and an older model has no such attributes.
            Logger.LogDebug("Resources of adapter '{AdapterId}' are not shown: {Reason}", id, e.Message);
        }
    }
}
