using Microsoft.AspNetCore.SignalR;
using BSourceNotifier.Application;

namespace BSourceNotifier.Infrastructure.SignalR;

public sealed class NotificationHub : Hub
{
    private readonly NotificationPresenceRegistry _presence;

    public NotificationHub(NotificationPresenceRegistry presence) => _presence = presence;

    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        var userId = Context.User?.Identity?.Name
            ?? httpContext?.Request.Query["userId"].ToString();
        var applicationId = httpContext?.Request.Query["applicationId"].ToString();

        if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(applicationId))
        {
            _presence.Add(applicationId, userId, Context.ConnectionId);
            await Groups.AddToGroupAsync(Context.ConnectionId, NotificationPresenceRegistry.GroupName(applicationId, userId));
        }

        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _presence.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
