using System.Dynamic;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using BSourceNotifier.Application.Interfaces;
using BSourceNotifier.Domain.Entities;
using BSourceNotifier.Domain.Enums;
using BSourceNotifier.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using RazorLight;

namespace BSourceNotifier.Infrastructure.Channels;

public sealed class EmailNotificationChannel : INotificationChannel
{
    private readonly IOptions<NotificationOptions> _options;
    private readonly ILogger<EmailNotificationChannel> _logger;
    private static readonly RazorLightEngine RazorEngine = new RazorLightEngineBuilder()
        .UseMemoryCachingProvider()
        .Build();

    public EmailNotificationChannel(IOptions<NotificationOptions> options, ILogger<EmailNotificationChannel> logger)
    {
        _options = options;
        _logger = logger;
    }

    public NotificationChannelType ChannelType => NotificationChannelType.Email;

    public async Task SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        var settings = _options.Value.Email;
        if (!settings.Enabled)
        {
            return;
        }

        var emailTarget = notification.Target.Endpoints.Email;
        if (emailTarget is null || string.IsNullOrWhiteSpace(emailTarget.To))
        {
            throw new InvalidOperationException("Target email endpoint is required for email notifications.");
        }

        var model = NormalizeModel(notification.Target.Data);
        var body = await RazorEngine.CompileRenderStringAsync(
            notification.Id.ToString("N"),
            notification.Message,
            model);

        using var message = new MailMessage(settings.From, emailTarget.To)
        {
            Subject = notification.Title,
            Body = body,
            IsBodyHtml = true
        };

        using var client = new SmtpClient(settings.Smtp.Host, settings.Smtp.Port)
        {
            UseDefaultCredentials = false,
            Credentials = settings.Smtp.AuthenticationMode switch
            {
                SmtpAuthenticationMode.None => null,
                SmtpAuthenticationMode.Credentials => new NetworkCredential(settings.Smtp.Username, settings.Smtp.Password),
                _ => throw new InvalidOperationException("Unsupported SMTP AuthenticationMode.")
            },
            EnableSsl = settings.Smtp.EnableSsl
        };

        try
        {
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (SmtpException ex)
        {
            _logger.LogError(ex,
                "SMTP send failed for notification {NotificationId} at {SmtpHost}:{SmtpPort} using {AuthenticationMode}, STARTTLS {EnableSsl}, status {SmtpStatusCode}.",
                notification.Id, settings.Smtp.Host, settings.Smtp.Port,
                settings.Smtp.AuthenticationMode, settings.Smtp.EnableSsl, ex.StatusCode);
            throw;
        }
    }

    private static object NormalizeModel(object? data)
        => data switch
        {
            null => new object(),
            JsonElement jsonElement => ConvertJsonElement(jsonElement),
            _ => data
        };

    private static object? ConvertJsonElement(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => ConvertJsonObject(element),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var longValue)
                ? longValue
                : element.TryGetDecimal(out var decimalValue)
                    ? decimalValue
                    : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => null
        };

    private static ExpandoObject ConvertJsonObject(JsonElement element)
    {
        var expando = new ExpandoObject();
        var dictionary = (IDictionary<string, object?>)expando;

        foreach (var property in element.EnumerateObject())
        {
            dictionary[property.Name] = ConvertJsonElement(property.Value);
        }

        return expando;
    }
}
