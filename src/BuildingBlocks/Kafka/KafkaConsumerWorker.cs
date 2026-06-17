using BuildingBlocks.Events;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Kafka;

public abstract class KafkaConsumerWorker(
    IOptions<KafkaOptions> options,
    IKafkaProducer producer,
    ILogger logger) : BackgroundService
{
    private readonly KafkaOptions _options = options.Value;

    protected abstract IReadOnlyCollection<string> Topics { get; }
    protected abstract Task HandleAsync(string topic, string key, string value, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(Topics);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(stoppingToken);
                await HandleAsync(result.Topic, result.Message.Key, result.Message.Value, stoppingToken);
                consumer.Commit(result);
            }
            catch (ConsumeException ex)
            {
                logger.LogWarning(ex, "Kafka consume error");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Kafka handler failed");
                await producer.ProduceAsync(
                    KafkaTopics.DeadLetter(Topics.First()),
                    Guid.NewGuid().ToString("N"),
                    ex.ToString(),
                    stoppingToken);
            }
        }
    }
}
