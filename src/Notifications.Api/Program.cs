using BuildingBlocks.Kafka;
using BuildingBlocks.ServiceDefaults;
using Notifications.Api;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder
    .AddMarketplaceServiceDefaults("notifications-api")
    .AddPostgresDb<NotificationsDbContext>("notifications");

builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection(KafkaOptions.SectionName));
builder.Services.PostConfigure<KafkaOptions>(options =>
{
    options.ClientId = "notifications-api";
    options.ConsumerGroup = "notifications-api";
});
builder.Services.AddSingleton<IKafkaProducer, KafkaProducer>();
builder.Services.AddHostedService<NotificationConsumer>();

var app = builder.Build();

app.UseMarketplaceServiceDefaults();

app.MapGet("/notifications", async (NotificationsDbContext db, CancellationToken cancellationToken) =>
{
    var notifications = await db.NotificationLogs
        .AsNoTracking()
        .OrderByDescending(notification => notification.CreatedAtUtc)
        .Take(200)
        .Select(notification => notification.ToResponse())
        .ToListAsync(cancellationToken);

    return Results.Ok(notifications);
}).RequireAuthorization("AdminOnly").WithTags("Notifications");

app.MapGet("/notifications/topics/{topic}", async (string topic, NotificationsDbContext db, CancellationToken cancellationToken) =>
{
    var notifications = await db.NotificationLogs
        .AsNoTracking()
        .Where(notification => notification.Topic == topic)
        .OrderByDescending(notification => notification.CreatedAtUtc)
        .Take(100)
        .Select(notification => notification.ToResponse())
        .ToListAsync(cancellationToken);

    return Results.Ok(notifications);
}).RequireAuthorization("AdminOnly").WithTags("Notifications");

app.Run();

public partial class Program;
