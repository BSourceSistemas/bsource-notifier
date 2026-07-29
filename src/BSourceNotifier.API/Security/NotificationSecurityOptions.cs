namespace BSourceNotifier.API.Security;

public sealed class NotificationSecurityOptions
{
    public AuthorizedNotificationClient[] AuthorizedClients { get; init; } = Array.Empty<AuthorizedNotificationClient>();
}

public sealed class AuthorizedNotificationClient
{
    public string Name { get; init; } = string.Empty;
    public string ApiKeyHash { get; init; } = string.Empty;
}
