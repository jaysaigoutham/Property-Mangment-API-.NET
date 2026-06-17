using BuildingBlocks.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildingBlocks.Persistence;

public static class EntityConfigurationExtensions
{
    public static void ConfigureOutbox(this EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Topic).HasMaxLength(200).IsRequired();
        builder.Property(message => message.Type).HasMaxLength(200).IsRequired();
        builder.Property(message => message.Key).HasMaxLength(200).IsRequired();
        builder.Property(message => message.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(message => message.CreatedAtUtc).IsRequired();
        builder.HasIndex(message => new { message.ProcessedAtUtc, message.CreatedAtUtc });
    }
}
