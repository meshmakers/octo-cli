using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class GetAdapterCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _idArg;
    private readonly IArgument _jsonArg;
    private readonly IArgument _showSecretsArg;

    public GetAdapterCommand(ILogger<GetAdapterCommand> logger, IOptions<OctoToolOptions> options,
        IConsoleService consoleService,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "GetAdapter",
            "Gets the configuration for a specific adapter.", options,
            communicationServicesClient, authenticationService)
    {
        _consoleService = consoleService;
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

        _consoleService.WriteLine(output.ToString(CommandArgumentValue.IsArgumentUsed(_jsonArg)
            ? Formatting.None
            : Formatting.Indented));
    }
}
