using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Security.Authentication;
using BSourceNotifier.Domain.Entities;
using BSourceNotifier.Domain.Enums;
using BSourceNotifier.Infrastructure.Channels;
using BSourceNotifier.Infrastructure.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace BSourceNotifier.API.Tests;

public sealed class EmailNotificationChannelTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("smtp-test-user", "smtp-test-password")]
    [InlineData("smtp-test-user", "")]
    public async Task None_uses_starttls_and_never_authenticates_even_when_auth_is_advertised(string username, string password)
    {
        await using var server = new SmtpTestServer();
        var channel = CreateChannel(server, SmtpAuthenticationMode.None, new CaptureLogger(), username, password);
        await channel.SendAsync(CreateNotification(), CancellationToken.None);
        var message = await server.Message.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(server.TlsEstablished);
        Assert.Equal(2, server.Commands.Count(command => command == "EHLO"));
        Assert.Contains("STARTTLS", server.Commands);
        Assert.DoesNotContain("AUTH", server.Commands);
        Assert.Contains("To: recipient@example.invalid", message);
        Assert.Contains("Hello SMTP", message);
    }

    [Fact]
    public async Task Credentials_authenticates_after_starttls_and_sends_message()
    {
        await using var server = new SmtpTestServer(requireAuth: true);
        var channel = CreateChannel(server, SmtpAuthenticationMode.Credentials, new CaptureLogger());
        await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.True(server.Authenticated);
        var commands = server.Commands.ToArray();
        Assert.True(Array.IndexOf(commands, "STARTTLS") < Array.IndexOf(commands, "AUTH"));
        Assert.Contains("Hello SMTP", await server.Message.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Missing_starttls_fails_without_auth_or_message_and_is_logged()
    {
        await using var server = new SmtpTestServer(advertiseStartTls: false);
        var logger = new CaptureLogger();
        var error = await Assert.ThrowsAsync<SmtpException>(() =>
            CreateChannel(server, SmtpAuthenticationMode.None, logger).SendAsync(CreateNotification(), CancellationToken.None));

        Assert.Contains("secure", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AUTH", server.Commands);
        Assert.DoesNotContain("DATA", server.Commands);
        AssertFailureLogged(logger, error, SmtpAuthenticationMode.None);
    }

    [Theory]
    [InlineData("untrusted")]
    [InlineData("wronghost")]
    public async Task Invalid_certificate_fails_normal_validation_and_is_logged(string certificate)
    {
        await using var server = new SmtpTestServer(certificate: certificate);
        var logger = new CaptureLogger();
        var error = await Assert.ThrowsAsync<SmtpException>(() =>
            CreateChannel(server, SmtpAuthenticationMode.None, logger).SendAsync(CreateNotification(), CancellationToken.None));

        Assert.Contains(ExceptionChain(error), ex => ex is AuthenticationException);
        Assert.DoesNotContain("AUTH", server.Commands);
        Assert.DoesNotContain("DATA", server.Commands);
        AssertFailureLogged(logger, error, SmtpAuthenticationMode.None);
    }

    [Fact]
    public async Task Rejected_credentials_fail_without_message_and_are_logged()
    {
        await using var server = new SmtpTestServer(requireAuth: true, rejectAuth: true);
        var logger = new CaptureLogger();
        var error = await Assert.ThrowsAsync<SmtpException>(() =>
            CreateChannel(server, SmtpAuthenticationMode.Credentials, logger).SendAsync(CreateNotification(), CancellationToken.None));

        // SmtpClient proceeds to MAIL after a rejected AUTH; the relay then returns 530.
        Assert.Contains("5.7.0 Authentication required", error.ToString());
        Assert.Equal(SmtpStatusCode.MustIssueStartTlsFirst, error.StatusCode);
        Assert.False(server.Authenticated);
        Assert.Contains("AUTH", server.Commands);
        Assert.DoesNotContain("DATA", server.Commands);
        AssertFailureLogged(logger, error, SmtpAuthenticationMode.Credentials);
    }

    [Theory]
    [InlineData("None", true)]
    [InlineData("Credentials", true)]
    [InlineData("None", false)]
    [InlineData("Credentials", false)]
    public async Task Http_preserves_accepted_contract_and_delivers_when_smtp_succeeds(string mode, bool advertiseStartTls)
    {
        await using var server = new SmtpTestServer(advertiseStartTls: advertiseStartTls, requireAuth: mode == "Credentials");
        await using var factory = new EmailApiFactory(server.Port, mode);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "0123456789abcdef0123456789abcdef");
        var response = await client.PostAsJsonAsync("/api/notifications/send", new
        {
            applicationId = "smtp-acceptance",
            title = "SMTP acceptance",
            message = "<p>Hello @Model.Name</p>",
            channels = new[] { BSourceNotifier.Contracts.Enums.NotificationChannelType.Email },
            target = new
            {
                userId = "smtp-recipient",
                endpoints = new { email = new { to = "recipient@example.invalid" } },
                data = new Dictionary<string, string> { ["Name"] = "SMTP" }
            }
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
        if (!advertiseStartTls)
        {
            Assert.Contains("EHLO", server.Commands);
            Assert.DoesNotContain("AUTH", server.Commands);
            Assert.DoesNotContain("DATA", server.Commands);
            Assert.False(server.Message.IsCompleted);
            return;
        }
        var message = await server.Message.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("Subject: SMTP acceptance", message);
        Assert.Contains("Hello SMTP", message);
        Assert.Equal(mode == "Credentials", server.Authenticated);
        if (mode == "None") Assert.DoesNotContain("AUTH", server.Commands);
    }

    private static EmailNotificationChannel CreateChannel(SmtpTestServer server, SmtpAuthenticationMode mode,
        CaptureLogger logger, string username = "smtp-test-user", string password = "smtp-test-password")
        => new(Options.Create(new NotificationOptions
        {
            Email = new EmailOptions
            {
                Enabled = true,
                From = "sender@example.invalid",
                Smtp = new SmtpOptions
                {
                    Host = "localhost", Port = server.Port, EnableSsl = true,
                    AuthenticationMode = mode, Username = username, Password = password
                }
            }
        }), logger);

    private static Notification CreateNotification() => new(
        Guid.NewGuid(), "smtp-acceptance", "SMTP acceptance", "<p>Hello SMTP</p>",
        [NotificationChannelType.Email],
        new NotificationTarget("smtp-recipient", new NotificationTargetEndpoints(
            new NotificationTargetEmailEndpoint("recipient@example.invalid"), null), null), DateTime.UtcNow);

    private static IEnumerable<Exception> ExceptionChain(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException) yield return current;
    }

    private static void AssertFailureLogged(CaptureLogger logger, SmtpException error, SmtpAuthenticationMode mode)
    {
        var entry = Assert.Single(logger.Entries);
        Assert.Same(error, entry.Error);
        Assert.Equal("localhost", entry.Fields["SmtpHost"]);
        Assert.Equal(mode, entry.Fields["AuthenticationMode"]);
        Assert.Equal(error.StatusCode, entry.Fields["SmtpStatusCode"]);
        Assert.IsType<Guid>(entry.Fields["NotificationId"]);
        Assert.DoesNotContain("smtp-test-user", entry.Message);
        Assert.DoesNotContain("smtp-test-password", entry.Message);
        Assert.DoesNotContain("Hello SMTP", entry.Message);
    }

    private sealed class CaptureLogger : ILogger<EmailNotificationChannel>
    {
        public List<(string Message, Exception? Error, Dictionary<string, object?> Fields)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((formatter(state, exception), exception,
                ((IEnumerable<KeyValuePair<string, object?>>)(object)state!).ToDictionary(item => item.Key, item => item.Value)));
    }

    private sealed class EmailApiFactory(int smtpPort, string mode) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notification:Security:AuthorizedClients:0:Name"] = "SMTP acceptance",
                ["Notification:Security:AuthorizedClients:0:ApiKeyHash"] = "3eb1bd439947eb762998e566ccc2e099c791118b2f40579cc4f7da2b5061b7f9",
                ["Notification:Email:Enabled"] = "true",
                ["Notification:Email:From"] = "sender@example.invalid",
                ["Notification:Email:Smtp:Host"] = "localhost",
                ["Notification:Email:Smtp:Port"] = smtpPort.ToString(),
                ["Notification:Email:Smtp:AuthenticationMode"] = mode,
                ["Notification:Email:Smtp:Username"] = "smtp-test-user",
                ["Notification:Email:Smtp:Password"] = "smtp-test-password",
                ["Notification:Email:Smtp:EnableSsl"] = "true"
            }));
        }
    }
}
