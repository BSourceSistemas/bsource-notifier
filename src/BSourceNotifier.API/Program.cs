using BSourceNotifier.Application.Interfaces;
using BSourceNotifier.Application.UseCases;
using BSourceNotifier.Application;
using BSourceNotifier.Infrastructure.Channels;
using BSourceNotifier.Infrastructure.Options;
using BSourceNotifier.Infrastructure.SignalR;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
const string CorsPolicyName = "ConfiguredCors";

builder.Configuration.AddJsonFile("serilog.json", optional: false, reloadOnChange: true);
builder.Logging.ClearProviders();
builder.Host.UseSerilog((context, services, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration).ReadFrom.Services(services));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();
builder.Services.AddSignalR();
builder.Services.AddSingleton<NotificationPresenceRegistry>();
var allowedOrigins = GetConfigValues(builder.Configuration, "Cors:AllowedOrigins");
var allowedOriginHosts = GetConfigValues(builder.Configuration, "Cors:AllowedOriginHosts");

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
        policy.SetIsOriginAllowed(origin => IsAllowedOrigin(origin, allowedOrigins, allowedOriginHosts))
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection("Notification"));

builder.Services.AddScoped<SendNotificationUseCase>();
builder.Services.AddScoped<INotificationChannel, WebSocketNotificationChannel>();
builder.Services.AddScoped<INotificationChannel, EmailNotificationChannel>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//app.UseHttpsRedirection();
app.UseCors(CorsPolicyName);
app.MapControllers();
app.MapHealthChecks("/health");
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();

static string[] GetConfigValues(IConfiguration configuration, string key)
{
    var sectionValues = configuration.GetSection(key)
        .GetChildren()
        .Select(child => child.Value)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Cast<string>();

    var rawValue = configuration[key];
    var inlineValues = string.IsNullOrWhiteSpace(rawValue)
        ? Array.Empty<string>()
        : rawValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    return sectionValues
        .Concat(inlineValues)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static bool IsAllowedOrigin(string origin, IReadOnlyCollection<string> allowedOrigins, IReadOnlyCollection<string> allowedOriginHosts)
{
    if (allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
    {
        return true;
    }

    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
    {
        return false;
    }

    return allowedOriginHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
}
