using System.Threading.Tasks;
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
    private readonly IConfigWriter _configWriter;
    private readonly IArgument _identityServicesUriArg;
    private readonly IArgument _tenantIdArg;

    public ConfigOctoCommand(ILogger<ConfigOctoCommand> logger, IOptions<OctoToolOptions> options,
        IConfigWriter configWriter)
        : base(logger, "Config", "Configures the tool.", options)
    {
        _configWriter = configWriter;

        _assetServicesUriArg = CommandArgumentValue.AddArgument("asu", "assetServicesUri",
            new[] { "URI of asset repository services (e. g. 'https://localhost:5001/')" }, 1);
        _botServicesUriArg = CommandArgumentValue.AddArgument("bsu", "bobServicesUri",
            new[] { "URI of bot services (e. g. 'https://localhost:5009/')" }, 1);
        _identityServicesUriArg = CommandArgumentValue.AddArgument("isu", "identityServicesUri",
            new[] { "URI of identity services (e. g. 'https://localhost:5003/')" }, true, 1);
        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId",
            new[] { "Id of tenant (e. g. 'myService')" }, 1);
    }

    public override Task Execute()
    {
        Logger.LogInformation("Configuring the tool");

        if (CommandArgumentValue.IsArgumentUsed(_tenantIdArg))
        {
            Options.Value.TenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower();
        }
        else
        {
            Options.Value.TenantId = null;
        }

        if (CommandArgumentValue.IsArgumentUsed(_assetServicesUriArg))
        {
            Options.Value.AssetServiceUrl =
                CommandArgumentValue.GetArgumentScalarValue<string>(_assetServicesUriArg).ToLower();
        }
        else
        {
            Options.Value.AssetServiceUrl = null;
        }

        if (CommandArgumentValue.IsArgumentUsed(_botServicesUriArg))
        {
            Options.Value.BotServiceUrl =
                CommandArgumentValue.GetArgumentScalarValue<string>(_botServicesUriArg).ToLower();
        }
        else
        {
            Options.Value.BotServiceUrl = null;
        }

        if (CommandArgumentValue.IsArgumentUsed(_identityServicesUriArg))
        {
            Options.Value.IdentityServiceUrl =
                CommandArgumentValue.GetArgumentScalarValue<string>(_identityServicesUriArg).ToLower();
        }

        _configWriter.WriteSettings(Constants.OctoToolUserFolderName);

        return Task.CompletedTask;
    }
}
