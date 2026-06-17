using BuildingBlocks.Events;
using Microsoft.EntityFrameworkCore;

namespace Notifications.Api;

public sealed class NotificationLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Topic { get; set; } = string.Empty;
    public string EventKey { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record NotificationLogResponse(Guid Id, string Topic, string EventKey, string Subject, string Body, DateTimeOffset CreatedAtUtc);

public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("notifications");

        modelBuilder.Entity<NotificationLog>(builder =>
        {
            builder.ToTable("notification_logs");
            builder.HasKey(notification => notification.Id);
            builder.Property(notification => notification.Topic).HasMaxLength(200).IsRequired();
            builder.Property(notification => notification.EventKey).HasMaxLength(200).IsRequired();
            builder.Property(notification => notification.Subject).HasMaxLength(300).IsRequired();
            builder.Property(notification => notification.Body).HasMaxLength(2000).IsRequired();
            builder.Property(notification => notification.Payload).HasColumnType("jsonb").IsRequired();
            builder.HasIndex(notification => notification.CreatedAtUtc);
        });
    }
}

public static class NotificationMapping
{
    public static NotificationLogResponse ToResponse(this NotificationLog notification) =>
        new(notification.Id, notification.Topic, notification.EventKey, notification.Subject, notification.Body, notification.CreatedAtUtc);
}
