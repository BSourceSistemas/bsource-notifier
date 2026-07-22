using BSourceNotifier.Application.Interfaces;
using BSourceNotifier.Application.UseCases;
using BSourceNotifier.Application;
using BSourceNotifier.Infrastructure.Channels;
using BSourceNotifier.Infrastructure.Options;
using BSourceNotifier.Infrastructure.SignalR;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevCors", policy =>
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod());
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
app.UseCors("DevCors");
app.MapControllers();
app.MapHealthChecks("/health");
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();
