using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GraphQL;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Common.Shared.DataTransferObjects;
using Meshmakers.Octo.Common.Shared.Services;
using Meshmakers.Octo.Frontend.Client.Tenants;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.NotificationMessages;

internal class ResetNotificationsInError : ServiceClientOctoCommand<ITenantClient>
{
    private readonly INotificationRepository _notificationRepository;
    private readonly ITenantClient _tenantClient;
    private readonly IArgument _type;


    public ResetNotificationsInError(ILogger<ResetNotificationsInError> logger, IOptions<OctoToolOptions> options,
        ITenantClient tenantClient,
        INotificationRepository notificationRepository, IAuthenticationService authenticationService)
        : base(logger, "ResetNotificationsInError", "Sets notifications in error to Pending.", options, tenantClient,
            authenticationService)
    {
        _tenantClient = tenantClient;
        _notificationRepository = notificationRepository;

        _type = CommandArgumentValue.AddArgument("t", "type",
            new[] { "Type of notification message, available is 'email' or 'sms'" }, true,
            1);
    }


    public override async Task Execute()
    {
        Logger.LogInformation(
            "Resetting notification messages at \'{ValueAssetServiceUrl}\' for tenant \'{ValueTenantId}\'",
            Options.Value.AssetServiceUrl, Options.Value.TenantId);

        var type = CommandArgumentValue.GetArgumentScalarValue<NotificationTypesDto>(_type);

        var filterList = new List<FieldFilterDto>
        {
            new()
            {
                AttributeName = nameof(NotificationMessageDto.SendStatus),
                Operator = FieldFilterOperatorDto.Equals,
                ComparisonValue = SendStatusDto.Error
            },
            new()
            {
                AttributeName = nameof(NotificationMessageDto.NotificationType),
                Operator = FieldFilterOperatorDto.Equals,
                ComparisonValue = type
            }
        };

        var getQuery = new GraphQLRequest
        {
            Query = GraphQl.GetNotifications,
            Variables = new { fieldFilters = filterList.ToArray() }
        };

        var getResult = await _tenantClient.SendQueryAsync<NotificationMessageDto>(getQuery);
        if (!getResult.Items.Any())
        {
            Logger.LogInformation("No notifications in error has been returned");
            return;
        }

        Logger.LogInformation("{Count} notification messages in error has been returned", getResult.Items.Count());

        foreach (var notificationMessageDto in getResult.Items)
        {
            notificationMessageDto.LastTryDateTime = DateTime.UtcNow.AddMinutes(-10);
            notificationMessageDto.SendStatus = SendStatusDto.Pending;
        }

        if (string.IsNullOrEmpty(Options.Value.TenantId))
        {
            Logger.LogError("No tenant id has been saved in configuration use --config to set a value");
            return;
        }

        await _notificationRepository.StoreNotificationMessages(Options.Value.TenantId, getResult.Items);

        Logger.LogInformation("Reset notification messages completed");
    }
}
