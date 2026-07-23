using BSourceNotifier.Domain.Enums;

namespace BSourceNotifier.Domain.Entities;

public sealed class Notification
{
    public Notification(
        Guid id,
        string applicationId,
        string title,
        string message,
        IReadOnlyCollection<NotificationChannelType> channels,
        NotificationTarget target,
        DateTime createdAt)
    {
        Id = id;
        ApplicationId = applicationId;
        Title = title;
        Message = message;
        Channels = channels;
        Target = target;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }
    public string ApplicationId { get; }
    public string Title { get; }
    public string Message { get; }
    public IReadOnlyCollection<NotificationChannelType> Channels { get; }
    public NotificationTarget Target { get; }
    public DateTime CreatedAt { get; }
}
