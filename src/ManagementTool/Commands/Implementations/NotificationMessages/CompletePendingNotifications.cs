using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Communication.Contracts.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.NotificationMessages;

internal class CompletePendingNotifications : ServiceClientOctoCommand<ITenantClient>
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IArgument _type;


    public CompletePendingNotifications(ILogger<CompletePendingNotifications> logger, IOptions<OctoToolOptions> options,
        INotificationRepository notificationRepository, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, "CompletePendingNotifications", "Sets the sent date time for pending notification message",
            options, tenantClient, authenticationService)
    {
        _notificationRepository = notificationRepository;

        _type = CommandArgumentValue.AddArgument("t", "type",
            ["Type of notification message, available is 'email' or 'sms'"], true,
            1);
    }


    public override async Task Execute()
    {
        Logger.LogInformation(
            "Completing notification messages at \'{ValueAssetServiceUrl}\' for tenant \'{ValueTenantId}\'",
            Options.Value.AssetServiceUrl, Options.Value.TenantId);
        if (string.IsNullOrEmpty(Options.Value.TenantId))
        {
            Logger.LogError("No tenant id has been saved in configuration use --config to set a value");
            return;
        }

        var type = CommandArgumentValue.GetArgumentScalarValue<NotificationTypesDto>(_type);

        var result = await _notificationRepository.GetPendingMessagesAsync(Options.Value.TenantId, type);
        foreach (var notificationMessageDto in result.List)
        {
            notificationMessageDto.SentDateTime = DateTime.UtcNow;
            notificationMessageDto.SendStatus = SendStatusDto.Sent;
        }

        await _notificationRepository.StoreNotificationMessages(Options.Value.TenantId, result.List);

        Logger.LogInformation("Notification message completed");
    }
}
