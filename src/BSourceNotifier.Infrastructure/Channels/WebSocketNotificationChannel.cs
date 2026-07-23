using BSourceNotifier.Application.Interfaces;
using BSourceNotifier.Application;
using BSourceNotifier.Domain.Entities;
using BSourceNotifier.Domain.Enums;
using BSourceNotifier.Infrastructure.Options;
using BSourceNotifier.Infrastructure.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BSourceNotifier.Infrastructure.Channels;

public sealed class WebSocketNotificationChannel : INotificationChannel
{
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly IOptions<NotificationOptions> _options;
    private readonly ILogger<WebSocketNotificationChannel> _logger;

    public WebSocketNotificationChannel(
        IHubContext<NotificationHub> hubContext,
        IOptions<NotificationOptions> options,
        ILogger<WebSocketNotificationChannel> logger)
    {
        _hubContext = hubContext;
        _options = options;
        _logger = logger;
    }

    public NotificationChannelType ChannelType => NotificationChannelType.WebSocket;

    public async Task SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        if (!_options.Value.WebSocket.Enabled)
        {
            return;
        }

        var groupName = notification.Target.Endpoints.WebSocket?.Group;
        if (string.IsNullOrWhiteSpace(groupName))
        {
            groupName = NotificationPresenceRegistry.GroupName(notification.ApplicationId, notification.Target.UserId);
        }
        _logger.LogInformation("Sending WebSocket notification {NotificationId} to group {GroupName}.", notification.Id, groupName);
        await _hubContext.Clients.Group(groupName).SendAsync(
            "notification",
            new
            {
                notification.Id,
                notification.Title,
                notification.Message,
                notification.CreatedAt,
                notification.Target.UserId,
                notification.Target.Data
            },
            cancellationToken);
    }
}
