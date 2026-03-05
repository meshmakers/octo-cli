using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.General.Context;

internal class AddContextCommand : Command<OctoToolOptions>
{
    private readonly IContextManager _contextManager;

    private readonly IArgument _nameArg;
    private readonly IArgument _identityServicesUriArg;
    private readonly IArgument _assetServicesUriArg;
    private readonly IArgument _botServicesUriArg;
    private readonly IArgument _communicationServicesUriArg;
    private readonly IArgument _reportingServicesUriArg;
    private readonly IArgument _adminPanelUriArg;
    private readonly IArgument _tenantIdArg;

    public AddContextCommand(ILogger<AddContextCommand> logger, IOptions<OctoToolOptions> options,
        IContextManager contextManager)
        : base(logger, Constants.ContextGroup, "AddContext", "Adds or updates a named context.", options)
    {
        _contextManager = contextManager;

        _nameArg = CommandArgumentValue.AddArgument("n", "name",
            ["Name of the context (e. g. 'dev', 'prod')"], true, 1);
        _identityServicesUriArg = CommandArgumentValue.AddArgument("isu", "identityServicesUri",
            ["URI of identity services (e. g. 'https://localhost:5003/')"], 1);
        _assetServicesUriArg = CommandArgumentValue.AddArgument("asu", "assetServicesUri",
            ["URI of asset repository services (e. g. 'https://localhost:5001/')"], 1);
        _botServicesUriArg = CommandArgumentValue.AddArgument("bsu", "botServicesUri",
            ["URI of bot services (e. g. 'https://localhost:5009/')"], 1);
        _communicationServicesUriArg = CommandArgumentValue.AddArgument("csu", "communicationServicesUri",
            ["URI of communication services (e. g. 'https://localhost:5015/')"], 1);
        _reportingServicesUriArg = CommandArgumentValue.AddArgument("rsu", "reportingServicesUri",
            ["URI of reporting services (e. g. 'https://localhost:5007/')"], 1);
        _adminPanelUriArg = CommandArgumentValue.AddArgument("apu", "adminPanelUri",
            ["URI of admin panel (e. g. 'https://localhost:5005/')"], 1);
        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId",
            ["Id of tenant (e. g. 'meshtest')"], 1);
    }

    public override Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg);

        Logger.LogInformation("Adding context '{ContextName}'", name);

        var toolOptions = new OctoToolOptions();

        if (CommandArgumentValue.IsArgumentUsed(_identityServicesUriArg))
        {
            toolOptions.IdentityServiceUrl =
                CommandArgumentValue.GetArgumentScalarValue<string>(_identityServicesUriArg).ToLower();
        }

        if (CommandArgumentValue.IsArgumentUsed(_assetServicesUriArg))
        {
            toolOptions.AssetServiceUrl =
                CommandArgumentValue.GetArgumentScalarValue<string>(_assetServicesUriArg).ToLower();
        }

        if (CommandArgumentValue.IsArgumentUsed(_botServicesUriArg))
        {
            toolOptions.BotServiceUrl =
                CommandArgumentValue.GetArgumentScalarValue<string>(_botServicesUriArg).ToLower();
        }

        if (CommandArgumentValue.IsArgumentUsed(_communicationServicesUriArg))
        {
            toolOptions.CommunicationServiceUrl =
                CommandArgumentValue.GetArgumentScalarValue<string>(_communicationServicesUriArg).ToLower();
        }

        if (CommandArgumentValue.IsArgumentUsed(_reportingServicesUriArg))
        {
            toolOptions.ReportingServiceUrl =
                CommandArgumentValue.GetArgumentScalarValue<string>(_reportingServicesUriArg).ToLower();
        }

        if (CommandArgumentValue.IsArgumentUsed(_adminPanelUriArg))
        {
            toolOptions.AdminPanelUrl =
                CommandArgumentValue.GetArgumentScalarValue<string>(_adminPanelUriArg).ToLower();
        }

        if (CommandArgumentValue.IsArgumentUsed(_tenantIdArg))
        {
            toolOptions.TenantId =
                CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower();
        }

        var entry = new ContextEntry
        {
            OctoToolOptions = toolOptions
        };

        _contextManager.AddOrUpdateContext(name, entry);

        var activeContextName = _contextManager.GetActiveContextName();
        Logger.LogInformation("Context '{ContextName}' added. Active context: '{ActiveContext}'", name,
            activeContextName);

        return Task.CompletedTask;
    }
}
