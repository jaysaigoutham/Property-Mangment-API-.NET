namespace BuildingBlocks.Kafka;

public interface IKafkaProducer
{
    Task ProduceAsync(string topic, string key, string value, CancellationToken cancellationToken = default);
}
