using Microsoft.Extensions.Options;

namespace BSourceNotifier.Infrastructure.Options;

public sealed class NotificationOptionsValidator : IValidateOptions<NotificationOptions>
{
    public ValidateOptionsResult Validate(string? name, NotificationOptions options)
    {
        if (!options.Email.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var smtp = options.Email.Smtp;
        var failures = new List<string>();
        if (!Enum.IsDefined(smtp.AuthenticationMode))
        {
            failures.Add("Notification:Email:Smtp:AuthenticationMode must be Credentials or None.");
        }
        else if (smtp.AuthenticationMode == SmtpAuthenticationMode.Credentials)
        {
            if (string.IsNullOrWhiteSpace(smtp.Username) || string.IsNullOrWhiteSpace(smtp.Password))
            {
                failures.Add("Notification:Email:Smtp:Username and Password are required when AuthenticationMode is Credentials.");
            }
        }
        else if (!smtp.EnableSsl)
        {
            failures.Add("Notification:Email:Smtp:EnableSsl must be true when AuthenticationMode is None (STARTTLS required).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
