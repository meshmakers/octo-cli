using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations;

internal class ConfigOctoCommand : Command<OctoToolOptions>
{
    private readonly IArgument _assetServicesUriArg;
    private readonly IArgument _botServicesUriArg;
    private readonly IArgument _communicationServicesUriArg;
    private readonly IConfigWriter _configWriter;
    private readonly IArgument _identityServicesUriArg;
    private readonly IArgument _adminPanelUriArg;
    private readonly IArgument _tenantIdArg;

    public ConfigOctoCommand(ILogger<ConfigOctoCommand> logger, IOptions<OctoToolOptions> options,
        IConfigWriter configWriter)
        : base(logger, "Config", "Configures the tool.", options)
    {
        _configWriter = configWriter;

        _assetServicesUriArg = CommandArgumentValue.AddArgument("asu", "assetServicesUri",
            ["URI of asset repository services (e. g. 'https://localhost:5001/')"], 1);
        _botServicesUriArg = CommandArgumentValue.AddArgument("bsu", "bobServicesUri",
            ["URI of bot services (e. g. 'https://localhost:5009/')"], 1);
        _identityServicesUriArg = CommandArgumentValue.AddArgument("isu", "identityServicesUri",
            ["URI of identity services (e. g. 'https://localhost:5003/')"], true, 1);
        _communicationServicesUriArg = CommandArgumentValue.AddArgument("csu", "communicationServicesUri",
            ["URI of communication services (e. g. 'https://localhost:5015/')"], 1);
        _adminPanelUriArg = CommandArgumentValue.AddArgument("apu", "adminPanelUri",
            ["URI of admin panel (e. g. 'https://localhost:5005/')"], 1);
        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId",
            ["Id of tenant (e. g. 'meshtest')"], 1);
    }

    public override Task Execute()
    {
        Logger.LogInformation("Configuring the tool");

        Options.Value.TenantId = CommandArgumentValue.IsArgumentUsed(_tenantIdArg) ?
            CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower() : null;

        Options.Value.AssetServiceUrl = CommandArgumentValue.IsArgumentUsed(_assetServicesUriArg) ?
            CommandArgumentValue.GetArgumentScalarValue<string>(_assetServicesUriArg).ToLower() : null;

        Options.Value.BotServiceUrl = CommandArgumentValue.IsArgumentUsed(_botServicesUriArg) ?
            CommandArgumentValue.GetArgumentScalarValue<string>(_botServicesUriArg).ToLower() : null;
        
        Options.Value.CommunicationServiceUrl = CommandArgumentValue.IsArgumentUsed(_communicationServicesUriArg) ? 
            CommandArgumentValue.GetArgumentScalarValue<string>(_communicationServicesUriArg).ToLower() : null;

        Options.Value.AdminPanelUrl = CommandArgumentValue.IsArgumentUsed(_adminPanelUriArg) ? 
            CommandArgumentValue.GetArgumentScalarValue<string>(_adminPanelUriArg).ToLower() : null;

        if (CommandArgumentValue.IsArgumentUsed(_identityServicesUriArg))
        {
            Options.Value.IdentityServiceUrl =
                CommandArgumentValue.GetArgumentScalarValue<string>(_identityServicesUriArg).ToLower();
        }

        _configWriter.WriteSettings(Constants.OctoToolUserFolderName);

        return Task.CompletedTask;
    }
}
