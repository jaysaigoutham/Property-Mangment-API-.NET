using System.Text.Json;
using BuildingBlocks.Events;
using BuildingBlocks.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Notifications.Api;

public sealed class NotificationConsumer(
    IOptions<KafkaOptions> options,
    IKafkaProducer producer,
    ILogger<NotificationConsumer> logger,
    IServiceScopeFactory scopeFactory) : KafkaConsumerWorker(options, producer, logger)
{
    protected override IReadOnlyCollection<string> Topics =>
    [
        KafkaTopics.UserRegistered,
        KafkaTopics.ListingApproved,
        KafkaTopics.ListingRejected,
        KafkaTopics.InquiryCreated,
        KafkaTopics.ReviewCreated,
        KafkaTopics.NotificationRequested
    ];

    protected override async Task HandleAsync(string topic, string key, string value, CancellationToken cancellationToken)
    {
        var (subject, body) = BuildMessage(topic, value);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        db.NotificationLogs.Add(new NotificationLog
        {
            Topic = topic,
            EventKey = key,
            Subject = subject,
            Body = body,
            Payload = value
        });

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Notification recorded for {Topic}: {Subject}", topic, subject);
    }

    private static (string Subject, string Body) BuildMessage(string topic, string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        return topic switch
        {
            KafkaTopics.UserRegistered => ("Welcome to the property marketplace", "Your account is ready."),
            KafkaTopics.ListingApproved => ("Listing approved", "A listing was approved and is now public."),
            KafkaTopics.ListingRejected => ("Listing rejected", "A listing needs changes before publication."),
            KafkaTopics.InquiryCreated => ("New property inquiry", "A buyer submitted an inquiry."),
            KafkaTopics.ReviewCreated => ("New review submitted", "A review is waiting for moderation."),
            KafkaTopics.NotificationRequested => ReadRequestedNotification(root),
            _ => ("Marketplace event received", $"Event topic: {topic}")
        };
    }

    private static (string Subject, string Body) ReadRequestedNotification(JsonElement envelope)
    {
        if (!envelope.TryGetProperty("Payload", out var payload))
        {
            return ("Notification requested", "A marketplace notification was requested.");
        }

        var subject = payload.TryGetProperty("Subject", out var subjectElement)
            ? subjectElement.GetString() ?? "Notification requested"
            : "Notification requested";

        var body = payload.TryGetProperty("Body", out var bodyElement)
            ? bodyElement.GetString() ?? string.Empty
            : string.Empty;

        return (subject, body);
    }
}
