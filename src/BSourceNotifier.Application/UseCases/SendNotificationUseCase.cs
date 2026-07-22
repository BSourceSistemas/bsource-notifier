using BSourceNotifier.Application.Interfaces;
using BSourceNotifier.Contracts.Commands;
using BSourceNotifier.Domain.Entities;
using BSourceNotifier.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace BSourceNotifier.Application.UseCases;

public sealed class SendNotificationUseCase
{
    private readonly IEnumerable<INotificationChannel> _channels;
    private readonly ILogger<SendNotificationUseCase> _logger;
    private readonly NotificationPresenceRegistry _presence;

    public SendNotificationUseCase(IEnumerable<INotificationChannel> channels, NotificationPresenceRegistry presence, ILogger<SendNotificationUseCase> logger)
    {
        _channels = channels;
        _presence = presence;
        _logger = logger;
    }

    public async Task ExecuteAsync(SendNotificationCommand command, CancellationToken cancellationToken)
    {
        var channelTypes = command.Channels.Select(MapChannelType).ToArray();

        var notification = new Notification(
            Guid.NewGuid(),
            command.Title,
            command.Message,
            channelTypes,
            new NotificationTarget(
                command.Target.UserId,
                new NotificationTargetEndpoints(
                    command.Target.Endpoints.Email is null
                        ? null
                        : new NotificationTargetEmailEndpoint(command.Target.Endpoints.Email.To),
                    command.Target.Endpoints.WebSocket is null
                        ? null
                        : new NotificationTargetWebSocketEndpoint(command.Target.Endpoints.WebSocket.Group)),
                command.Target.Data),
            DateTime.UtcNow);

        foreach (var channelType in notification.Channels)
        {
            var channel = _channels.FirstOrDefault(c => c.ChannelType == channelType);
            if (channel is null)
            {
                _logger.LogWarning("Channel {ChannelType} not registered.", channelType);
                continue;
            }

            try
            {
                await channel.SendAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send notification {NotificationId} via {ChannelType}.", notification.Id, channelType);
            }
        }

        if (command.EmailFallbackWhenOffline &&
            !string.IsNullOrWhiteSpace(command.ApplicationId) &&
            !_presence.IsOnline(command.ApplicationId, command.Target.UserId) &&
            !notification.Channels.Contains(NotificationChannelType.Email))
        {
            var email = _channels.FirstOrDefault(c => c.ChannelType == NotificationChannelType.Email);
            if (email is not null)
                await email.SendAsync(notification, cancellationToken);
        }
    }

    private static NotificationChannelType MapChannelType(Contracts.Enums.NotificationChannelType channelType)
        => channelType switch
        {
            Contracts.Enums.NotificationChannelType.WebSocket => NotificationChannelType.WebSocket,
            Contracts.Enums.NotificationChannelType.Email => NotificationChannelType.Email,
            Contracts.Enums.NotificationChannelType.Sms => NotificationChannelType.Sms,
            Contracts.Enums.NotificationChannelType.Telegram => NotificationChannelType.Telegram,
            Contracts.Enums.NotificationChannelType.WhatsApp => NotificationChannelType.WhatsApp,
            _ => throw new ArgumentOutOfRangeException(nameof(channelType), channelType, "Unsupported channel type.")
        };
}
