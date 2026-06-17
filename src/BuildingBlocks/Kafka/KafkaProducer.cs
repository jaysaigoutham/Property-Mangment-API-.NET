using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Kafka;

public sealed class KafkaProducer : IKafkaProducer, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaProducer> _logger;

    public KafkaProducer(IOptions<KafkaOptions> options, ILogger<KafkaProducer> logger)
    {
        _logger = logger;
        var kafka = options.Value;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            ClientId = kafka.ClientId,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageSendMaxRetries = 5
        }).Build();
    }

    public async Task ProduceAsync(string topic, string key, string value, CancellationToken cancellationToken = default)
    {
        var result = await _producer.ProduceAsync(
            topic,
            new Message<string, string> { Key = key, Value = value },
            cancellationToken);

        _logger.LogInformation("Published Kafka event {Topic} at {PartitionOffset}", topic, result.TopicPartitionOffset);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
