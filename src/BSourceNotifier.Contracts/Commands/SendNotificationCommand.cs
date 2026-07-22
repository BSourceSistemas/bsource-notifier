using BSourceNotifier.Contracts.Enums;
using BSourceNotifier.Contracts.Models;

namespace BSourceNotifier.Contracts.Commands;

public sealed class SendNotificationCommand
{
    public string ApplicationId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public NotificationChannelType[] Channels { get; init; } = Array.Empty<NotificationChannelType>();
    public NotificationTargetDto Target { get; init; } = new();
    public bool EmailFallbackWhenOffline { get; init; }
}
