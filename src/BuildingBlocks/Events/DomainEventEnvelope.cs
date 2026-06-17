using System.Text.Json;

namespace BuildingBlocks.Events;

public sealed record DomainEventEnvelope(
    Guid EventId,
    string Type,
    int Version,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string Producer,
    JsonElement Payload)
{
    public static DomainEventEnvelope Create<TPayload>(
        string type,
        string producer,
        TPayload payload,
        string? correlationId = null,
        int version = 1) =>
        new(
            Guid.NewGuid(),
            type,
            version,
            DateTimeOffset.UtcNow,
            string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId,
            producer,
            JsonSerializer.SerializeToElement(payload));
}
