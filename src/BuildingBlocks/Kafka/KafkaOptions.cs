namespace BuildingBlocks.Kafka;

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; set; } = "localhost:9092";
    public string ClientId { get; set; } = "property-marketplace";
    public string ConsumerGroup { get; set; } = "property-marketplace";
    public int OutboxBatchSize { get; set; } = 25;
    public int OutboxPollSeconds { get; set; } = 5;
}
