
/*
namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.NotificationMessages;

internal class CreateNotification : ServiceClientOctoCommand<ITenantClient>
{
    private readonly IArgument _associationCkId;
    private readonly IArgument _associationRtId;
    private readonly IArgument _body;
    private readonly INotificationRepository _notificationRepository;
    private readonly IArgument _recipient;
    private readonly IArgument _subject;
    private readonly IArgument _type;


    public CreateNotification(ILogger<CreateNotification> logger, IOptions<OctoToolOptions> options,
        INotificationRepository notificationRepository, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, "CreateNotification", "Create a new notification message", options, tenantClient,
            authenticationService)
    {
        _notificationRepository = notificationRepository;

        _type = CommandArgumentValue.AddArgument("t", "type",
            ["Type of notification message, available is 'email' or 'sms'"], true,
            1);

        _recipient = CommandArgumentValue.AddArgument("r", "recipient",
            ["Address of recipient (for example e-mail address or phone number)'"], true,
            1);

        _subject = CommandArgumentValue.AddArgument("s", "subject", ["Subject of notification message."],
            false, 1);

        _body = CommandArgumentValue.AddArgument("b", "body", ["Body of notification message"],
            true, 1);

        _associationCkId = CommandArgumentValue.AddArgument("ackid", "associationCkId",
            ["Association construction kit ID of related entity"],
            false, 1);
        _associationRtId = CommandArgumentValue.AddArgument("artid", "associationRtId",
            ["Association runtime ID of related entity"],
            false, 1);
    }


    public override async Task Execute()
    {
        var type = CommandArgumentValue.GetArgumentScalarValue<NotificationTypesDto>(_type);
        var recipient = CommandArgumentValue.GetArgumentScalarValue<string>(_recipient);
        var subject = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_subject);
        var body = CommandArgumentValue.GetArgumentScalarValue<string>(_body);
        var associationCkId = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_associationCkId);
        var associationRtId = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_associationRtId);

        RtEntityId? rtEntityId = null;
        if (!string.IsNullOrWhiteSpace(associationCkId) && !string.IsNullOrWhiteSpace(associationRtId))
        {
            rtEntityId = new RtEntityId(associationCkId, OctoObjectId.Parse(associationRtId));
        }

        Logger.LogInformation(
            "Creating notification messages at \'{ValueAssetServiceUrl}\' for tenant \'{ValueTenantId}\'",
            Options.Value.AssetServiceUrl, Options.Value.TenantId);
        if (string.IsNullOrEmpty(Options.Value.TenantId))
        {
            Logger.LogError("No tenant id has been saved in configuration use --config to set a value");
            return;
        }

        switch (type)
        {
            case NotificationTypesDto.Sms:
                await _notificationRepository.AddShortMessageAsync(Options.Value.TenantId, recipient, body, rtEntityId);
                break;
            case NotificationTypesDto.EMail:
                if (string.IsNullOrWhiteSpace(subject))
                {
                    Logger.LogError("Subject is missing");
                    return;
                }
                await _notificationRepository.AddEMailMessageAsync(Options.Value.TenantId, recipient, subject, body,
                    rtEntityId);
                break;
            default:
                throw new NotImplementedException();
        }

        Logger.LogInformation("Notification message added");
    }
}
*/