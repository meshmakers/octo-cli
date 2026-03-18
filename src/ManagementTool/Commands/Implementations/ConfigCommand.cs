using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations;

internal class ConfigOctoCommand : Command<OctoToolOptions>
{
    private readonly IArgument _adminPanelUriArg;
    private readonly IArgument _assetServicesUriArg;
    private readonly IArgument _botServicesUriArg;
    private readonly IArgument _communicationServicesUriArg;
    private readonly IContextManager _contextManager;
    private readonly IArgument _identityServicesUriArg;
    private readonly IArgument _reportingServicesUriArg;
    private readonly IArgument _tenantIdArg;

    public ConfigOctoCommand(ILogger<ConfigOctoCommand> logger, IOptions<OctoToolOptions> options,
        IContextManager contextManager)
        : base(logger, "Config", "Configures the active context.", options)
    {
        _contextManager = contextManager;

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
        _reportingServicesUriArg = CommandArgumentValue.AddArgument("rsu", "reportingServicesUri",
            ["URI of reporting services (e. g. 'https://localhost:5007/')"], 1);
    }

    public override Task Execute()
    {
        Logger.LogInformation("Configuring the tool");

        Options.Value.TenantId = CommandArgumentValue.IsArgumentUsed(_tenantIdArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower()
            : null;

        Options.Value.AssetServiceUrl = CommandArgumentValue.IsArgumentUsed(_assetServicesUriArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_assetServicesUriArg).ToLower()
            : null;

        Options.Value.BotServiceUrl = CommandArgumentValue.IsArgumentUsed(_botServicesUriArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_botServicesUriArg).ToLower()
            : null;

        Options.Value.CommunicationServiceUrl = CommandArgumentValue.IsArgumentUsed(_communicationServicesUriArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_communicationServicesUriArg).ToLower()
            : null;

        Options.Value.ReportingServiceUrl = CommandArgumentValue.IsArgumentUsed(_reportingServicesUriArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_reportingServicesUriArg).ToLower()
            : null;

        Options.Value.AdminPanelUrl = CommandArgumentValue.IsArgumentUsed(_adminPanelUriArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_adminPanelUriArg).ToLower()
            : null;

        if (CommandArgumentValue.IsArgumentUsed(_identityServicesUriArg))
        {
            Options.Value.IdentityServiceUrl =
                CommandArgumentValue.GetArgumentScalarValue<string>(_identityServicesUriArg).ToLower();
        }

        // Save to active context, creating a default if none exists
        var activeContext = _contextManager.GetActiveContext();
        if (activeContext != null)
        {
            activeContext.OctoToolOptions = Options.Value;
            _contextManager.SaveActiveContext();
        }
        else
        {
            var entry = new ContextEntry
            {
                OctoToolOptions = Options.Value
            };
            _contextManager.AddOrUpdateContext("default", entry);
        }

        return Task.CompletedTask;
    }
}