using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AdminPanel.System;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Diagnostics;

public class ReconfigureLogLevel : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IAssetServicesClient _assetServicesClient;
    private readonly IBotServicesClient _botServicesClient;
    private readonly ICommunicationServicesClient _communicationServicesClient;
    private readonly IAdminPanelClient _adminPanelClient;
    private readonly IArgument _serviceName;
    private readonly IArgument _minLogLevel;
    private readonly IArgument _maxLogLevel;
    private readonly IArgument _loggerName;

    public ReconfigureLogLevel(ILogger<ReconfigureLogLevel> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService,
        IAssetServicesClient assetServicesClient, IBotServicesClient botServicesClient,
        ICommunicationServicesClient communicationServicesClient, IAdminPanelClient adminPanelClient)
        : base(logger, "ReconfigureLogLevel", "Reconfigures the log level for services", options,
            identityServicesClient, authenticationService)
    {
        _assetServicesClient = assetServicesClient;
        _botServicesClient = botServicesClient;
        _communicationServicesClient = communicationServicesClient;
        _adminPanelClient = adminPanelClient;
        _serviceName = CommandArgumentValue.AddArgument("n", "serviceName", [
            "The service name to configure, " +
            "allowed is 'Identity', 'AssetRepository', 'Bot', 'CommunicationController', 'AdminPanel'"
        ], true, 1);
        _minLogLevel = CommandArgumentValue.AddArgument("minL", "minLogLevel", [
            "The minimal log level to set for the services, " +
            "allowed is 'Trace', 'Debug', 'Info', 'Warn', 'Error', 'Fatal', 'Off'"
        ], true, 1);
        _maxLogLevel = CommandArgumentValue.AddArgument("maxL", "maxLogLevel", [
            "The maximum log level to set for the services, " +
            "allowed is 'Trace', 'Debug', 'Info', 'Warn', 'Error', 'Fatal', 'Off'"
        ], true, 1);
        _loggerName = CommandArgumentValue.AddArgument("ln", "loggerName", [
            "The logger name, " +
            "allowed is 'Microsoft.*', 'Meshmakers.*', 'Masstransit.*', '*'"
        ], true, 1);
    }

    public override async Task Execute()
    {
        var serviceName = CommandArgumentValue.GetArgumentScalarValue<string>(_serviceName).ToLower();
        var minLogLevel = CommandArgumentValue.GetArgumentScalarValue<LogLevelDto>(_minLogLevel);
        var maxLogLevel = CommandArgumentValue.GetArgumentScalarValue<LogLevelDto>(_maxLogLevel);
        var loggerName = CommandArgumentValue.GetArgumentScalarValue<string>(_loggerName);

        Logger.LogInformation(
            "Setting log level for logger '{LoggerName}' to minimum '{MinLogLevel}' and maximum '{MaxLogLevel}' for service \'{ServiceName}\'",
            loggerName, minLogLevel, maxLogLevel, serviceName);
        
        switch (serviceName)
        {
            case "identity":
                Logger.LogInformation("URI: '{ServiceUri}'", ServiceClient.ServiceUri);
                await ServiceClient.ReconfigureLogLevelAsync(loggerName, minLogLevel, maxLogLevel);
                break;
            case "assetrepository":
                Logger.LogInformation("URI: '{ServiceUri}'", _assetServicesClient.ServiceUri);
                await _assetServicesClient.ReconfigureLogLevelAsync(loggerName, minLogLevel, maxLogLevel);
                break;                
            case "bot":
                Logger.LogInformation("URI: '{ServiceUri}'", _botServicesClient.ServiceUri);
                await _botServicesClient.ReconfigureLogLevelAsync(loggerName, minLogLevel, maxLogLevel);
                break;
            case "communicationcontroller":
                Logger.LogInformation("URI: '{ServiceUri}'", _communicationServicesClient.ServiceUri);
                await _communicationServicesClient.ReconfigureLogLevelAsync(loggerName, minLogLevel, maxLogLevel);
                break;
            case "adminpanel":
                Logger.LogInformation("URI: '{ServiceUri}'", _adminPanelClient.ServiceUri);
                await _adminPanelClient.ReconfigureLogLevelAsync(loggerName, minLogLevel, maxLogLevel);
                break;
        }
        
        Logger.LogInformation("Reconfiguration done");
    }
}