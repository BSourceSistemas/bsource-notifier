using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using BSourceNotifier.API.Security;
using BSourceNotifier.Contracts.Commands;
using BSourceNotifier.Contracts.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace BSourceNotifier.API.Tests;

public sealed class NotificationApiKeyAuthenticationTests
{
    private const string ValidApiKey = "0123456789abcdef0123456789abcdef";
    private const string ValidApiKeyHash = "3eb1bd439947eb762998e566ccc2e099c791118b2f40579cc4f7da2b5061b7f9";

    [Fact]
    public async Task Send_without_api_key_returns_unauthorized()
    {
        await using var factory = new NotifierApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/notifications/send", CreateCommand());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal(401, problem!.Status);
    }

    [Fact]
    public async Task Send_with_invalid_api_key_returns_forbidden()
    {
        await using var factory = new NotifierApiFactory();
        using var client = factory.CreateClient();

        using var request = CreateSendRequest();
        request.Headers.Add("X-Api-Key", "invalid-api-key");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal(403, problem!.Status);
    }

    [Fact]
    public async Task Send_with_valid_api_key_returns_accepted()
    {
        await using var factory = new NotifierApiFactory();
        using var client = factory.CreateClient();

        using var request = CreateSendRequest();
        request.Headers.Add("X-Api-Key", ValidApiKey);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Theory]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    [InlineData(" 0123456789abcdef0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcdef ")]
    [InlineData("Bearer 0123456789abcdef0123456789abcdef")]
    public async Task Send_rejects_casing_spaces_and_undocumented_prefixes(string apiKey)
    {
        await using var factory = new NotifierApiFactory();
        using var client = factory.CreateClient();

        using var request = CreateSendRequest();
        request.Headers.Add("X-Api-Key", apiKey);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Health_and_swagger_are_public_in_development()
    {
        await using var factory = new NotifierApiFactory();
        using var client = factory.CreateClient();

        var health = await client.GetAsync("/health");
        var swagger = await client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
    }

    [Fact]
    public async Task Authentication_logs_do_not_include_key_hash_or_sensitive_payload()
    {
        var logSink = new TestLogSink();
        var filter = new ApiKeyAuthorizationFilter(
            new Sha256ApiKeyValidator(new TestOptionsMonitor(new NotificationSecurityOptions
            {
                AuthorizedClients =
                [
                    new AuthorizedNotificationClient
                    {
                        Name = "Aplicacao1",
                        ApiKeyHash = ValidApiKeyHash
                    }
                ]
            })),
            new TestLogger<ApiKeyAuthorizationFilter>(nameof(ApiKeyAuthorizationFilter), logSink));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Api-Key"] = ValidApiKey;
        var actionContext = new Microsoft.AspNetCore.Mvc.ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var executingContext = new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), controller: null!);

        await filter.OnActionExecutionAsync(
            executingContext,
            () => Task.FromResult(new ActionExecutedContext(actionContext, [], controller: null!)));

        var logs = string.Join(Environment.NewLine, logSink.Messages);
        Assert.Contains("Aplicacao1", logs);
        Assert.Contains("Valid", logs);
        Assert.DoesNotContain(ValidApiKey, logs);
        Assert.DoesNotContain(ValidApiKeyHash, logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sensitive-reset-token", logs);
    }

    private static HttpRequestMessage CreateSendRequest()
        => new(HttpMethod.Post, "/api/notifications/send")
        {
            Content = JsonContent.Create(CreateCommand())
        };

    private static SendNotificationCommand CreateCommand()
        => new()
        {
            ApplicationId = "Aplicacao1",
            Title = "Password recovery",
            Message = "Use token {{token}}",
            Channels = Array.Empty<Contracts.Enums.NotificationChannelType>(),
            Target = new NotificationTargetDto
            {
                UserId = "user-123",
                Data = new
                {
                    Token = "sensitive-reset-token"
                },
                Endpoints = new NotificationTargetEndpointsDto()
            }
        };

    private sealed class NotifierApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Notification:Security:AuthorizedClients:0:Name"] = "Aplicacao1",
                    ["Notification:Security:AuthorizedClients:0:ApiKeyHash"] = ValidApiKeyHash,
                    ["Notification:Email:Enabled"] = "false",
                    ["Notification:WebSocket:Enabled"] = "false"
                });
            });
        }
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<NotificationSecurityOptions>
    {
        public TestOptionsMonitor(NotificationSecurityOptions currentValue)
        {
            CurrentValue = currentValue;
        }

        public NotificationSecurityOptions CurrentValue { get; }

        public NotificationSecurityOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<NotificationSecurityOptions, string?> listener) => null;
    }

    private sealed class TestLogSink
    {
        public ConcurrentQueue<string> Messages { get; } = new();
    }

    private class TestLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly TestLogSink _sink;

        public TestLogger(string categoryName, TestLogSink sink)
        {
            _categoryName = categoryName;
            _sink = sink;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _sink.Messages.Enqueue($"{logLevel} {_categoryName}: {formatter(state, exception)}");
        }
    }

    private sealed class TestLogger<T> : TestLogger, ILogger<T>
    {
        public TestLogger(string categoryName, TestLogSink sink)
            : base(categoryName, sink)
        {
        }
    }
}
