using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
/*
namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.NotificationMessages;

internal class GetNotificationMessages : ServiceClientOctoCommand<ITenantClient>
{
    private readonly IConsoleService _consoleService;
    private readonly INotificationRepository _notificationRepository;
    private readonly IArgument _type;

    public GetNotificationMessages(ILogger<GetNotificationMessages> logger,
        IConsoleService consoleService,
        IOptions<OctoToolOptions> options, INotificationRepository notificationRepository, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, "GetNotifications", "Gets all pending notification messages.", options, tenantClient,
            authenticationService)
    {
        _consoleService = consoleService;
        _notificationRepository = notificationRepository;

        _type = CommandArgumentValue.AddArgument("t", "type",
            ["Type of notification message, available is 'email' or 'sms'"], true,
            1);
    }

    public override async Task Execute()
    {
        Logger.LogInformation(
            "Getting pending notification messages from \'{ValueAssetServiceUrl}\' for tenant \'{ValueTenantId}\'",
            Options.Value.AssetServiceUrl, Options.Value.TenantId);

        if (string.IsNullOrEmpty(Options.Value.TenantId))
        {
            Logger.LogError("No tenant id has been saved in configuration use --config to set a value");
            return;
        }

        var type = CommandArgumentValue.GetArgumentScalarValue<NotificationTypesDto>(_type);

        var getResult = await _notificationRepository.GetPendingMessagesAsync(Options.Value.TenantId, type);
        if (!getResult.List.Any())
        {
            Logger.LogInformation("No notification messages has been returned");
            return;
        }

        var resultString = JsonConvert.SerializeObject(getResult.List, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }
}
*/