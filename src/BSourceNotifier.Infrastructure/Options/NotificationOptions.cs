namespace BSourceNotifier.Infrastructure.Options;

public sealed class NotificationOptions
{
    public WebSocketOptions WebSocket { get; init; } = new();
    public EmailOptions Email { get; init; } = new();
    public ChannelToggleOptions Sms { get; init; } = new();
    public ChannelToggleOptions Telegram { get; init; } = new();
    public ChannelToggleOptions WhatsApp { get; init; } = new();
}

public sealed class WebSocketOptions : ChannelToggleOptions
{
}

public sealed class EmailOptions : ChannelToggleOptions
{
    public string From { get; init; } = string.Empty;
    public SmtpOptions Smtp { get; init; } = new();
}

public sealed class SmtpOptions
{
    public SmtpAuthenticationMode AuthenticationMode { get; init; } = SmtpAuthenticationMode.Credentials;
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public bool EnableSsl { get; init; }
}

public enum SmtpAuthenticationMode
{
    Credentials,
    None
}

public class ChannelToggleOptions
{
    public bool Enabled { get; init; }
}
