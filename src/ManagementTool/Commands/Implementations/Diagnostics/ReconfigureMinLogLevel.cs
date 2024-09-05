using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Diagnostics;

public class ReconfigureMinLogLevel : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IAssetServicesClient _assetServicesClient;
    private readonly IBotServicesClient _botServicesClient;
    private readonly ICommunicationServicesClient _communicationServicesClient;
    private readonly IArgument _logLevel;

    public ReconfigureMinLogLevel(ILogger<ReconfigureMinLogLevel> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService,
        IAssetServicesClient assetServicesClient, IBotServicesClient botServicesClient,
        ICommunicationServicesClient communicationServicesClient)
        : base(logger, "ReconfigureMinLogLevel", "Reconfigures the minimal log level for services", options,
            identityServicesClient, authenticationService)
    {
        _assetServicesClient = assetServicesClient;
        _botServicesClient = botServicesClient;
        _communicationServicesClient = communicationServicesClient;
        _logLevel = CommandArgumentValue.AddArgument("l", "logLevel", [
            "The minimal log level to set for the services, " +
            "allowed is 'Trace', 'Debug', 'Info', 'Warn', 'Error', 'Fatal', 'Off'"
        ], true, 1);
    }

    public override async Task Execute()
    {
        var logLevelDto = CommandArgumentValue.GetArgumentScalarValue<LogLevelDto>(_logLevel);

        Logger.LogInformation(
            "Setting minimal LogLevel to \'{LogLevel}\' for Communication Controller Service at \'{ServiceClientServiceUri}\'",
            logLevelDto, _communicationServicesClient.ServiceUri);
        await _communicationServicesClient.ReconfigureLogLevelAsync(logLevelDto);
        
        Logger.LogInformation(
            "Setting minimal LogLevel to \'{LogLevel}\' for Asset Repository Service at \'{ServiceClientServiceUri}\'",
            logLevelDto, _assetServicesClient.ServiceUri);
        await _assetServicesClient.ReconfigureLogLevelAsync(logLevelDto);
        
        Logger.LogInformation(
            "Setting minimal LogLevel to \'{LogLevel}\' for Identity Service at \'{ServiceClientServiceUri}\'",
            logLevelDto, ServiceClient.ServiceUri);
        await ServiceClient.ReconfigureLogLevelAsync(logLevelDto);
        
        Logger.LogInformation(
            "Setting minimal LogLevel to \'{LogLevel}\' for Bot Service at \'{ServiceClientServiceUri}\'",
            logLevelDto, _botServicesClient.ServiceUri);
        await _botServicesClient.ReconfigureLogLevelAsync(logLevelDto);

        Logger.LogInformation("Minimal log level reconfiguration done");
    }
}