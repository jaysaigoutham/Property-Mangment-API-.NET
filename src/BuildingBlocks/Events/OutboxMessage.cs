using System.Text.Json;

namespace BuildingBlocks.Events;

public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Topic { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }

    public static OutboxMessage Create<TPayload>(
        string topic,
        string type,
        string producer,
        TPayload payload,
        string key,
        string? correlationId = null)
    {
        var envelope = DomainEventEnvelope.Create(type, producer, payload, correlationId);

        return new OutboxMessage
        {
            Topic = topic,
            Type = type,
            Key = key,
            Payload = JsonSerializer.Serialize(envelope)
        };
    }
}
