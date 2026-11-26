using BSourceNotifier.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace BSourceNotifier.API.Tests;

public sealed class SmtpOptionsTests
{
    [Fact]
    public void Missing_mode_defaults_to_credentials()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Email:Smtp:Username"] = "user",
            ["Email:Smtp:Password"] = "password"
        }).Build();
        var options = configuration.Get<NotificationOptions>()!;
        Assert.Equal(SmtpAuthenticationMode.Credentials, options.Email.Smtp.AuthenticationMode);
    }

    [Theory]
    [InlineData("Credentials", "user", "password", true, true, true)]
    [InlineData("Credentials", "user", "password", false, true, true)]
    [InlineData("Credentials", "", "password", true, true, false)]
    [InlineData("Credentials", "user", "", true, true, false)]
    [InlineData("Credentials", " ", "password", true, true, false)]
    [InlineData("Credentials", "user", " ", true, true, false)]
    [InlineData("Credentials", "", "", true, true, false)]
    [InlineData("None", "", "", true, true, true)]
    [InlineData("None", "user", "", true, true, true)]
    [InlineData("None", "", "password", true, true, true)]
    [InlineData("None", "user", "password", true, true, true)]
    [InlineData("None", "", "", false, true, false)]
    [InlineData("42", "user", "password", true, true, false)]
    [InlineData("Credentials", "", "", false, false, true)]
    [InlineData("None", "", "", false, false, true)]
    public async Task Configuration_is_validated_on_start(string mode, string username, string password,
        bool ssl, bool enabled, bool valid)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Notification:Email:Enabled"] = enabled.ToString(),
            ["Notification:Email:Smtp:AuthenticationMode"] = mode,
            ["Notification:Email:Smtp:Username"] = username,
            ["Notification:Email:Smtp:Password"] = password,
            ["Notification:Email:Smtp:EnableSsl"] = ssl.ToString()
        });
        builder.Services.AddSingleton<IValidateOptions<NotificationOptions>, NotificationOptionsValidator>();
        builder.Services.AddOptions<NotificationOptions>()
            .Bind(builder.Configuration.GetSection("Notification")).ValidateOnStart();
        using var host = builder.Build();
        if (valid)
        {
            await host.StartAsync();
            await host.StopAsync();
        }
        else
        {
            var error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
            Assert.Contains("Notification:Email:Smtp:", error.Message);
        }
    }

    [Fact]
    public void Unknown_mode_name_is_rejected_by_configuration_binding()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Email:Smtp:AuthenticationMode"] = "Anonymous"
        }).Build();
        var error = Assert.Throws<InvalidOperationException>(() => configuration.Get<NotificationOptions>());
        Assert.Contains("AuthenticationMode", error.Message);
    }
}
