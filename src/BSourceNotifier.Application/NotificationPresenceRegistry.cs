using System.Collections.Concurrent;

namespace BSourceNotifier.Application;

/// <summary>Tracks live connections generically by application and recipient.</summary>
public sealed class NotificationPresenceRegistry
{
    private readonly ConcurrentDictionary<string, (string ApplicationId, string RecipientId)> _connections = new();

    public void Add(string applicationId, string recipientId, string connectionId)
        => _connections[connectionId] = (applicationId, recipientId);

    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);

    public bool IsOnline(string applicationId, string recipientId)
        => _connections.Values.Any(x => x.ApplicationId == applicationId && x.RecipientId == recipientId);

    public static string GroupName(string applicationId, string recipientId)
        => $"app-{applicationId}-user-{recipientId}";
}
