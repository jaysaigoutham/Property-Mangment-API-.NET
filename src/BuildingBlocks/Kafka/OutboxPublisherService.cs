using BuildingBlocks.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Kafka;

public sealed class OutboxPublisherService<TContext>(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    ILogger<OutboxPublisherService<TContext>> logger) : BackgroundService
    where TContext : DbContext, IOutboxDbContext
{
    private readonly KafkaOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _options.OutboxPollSeconds)));

        while (!stoppingToken.IsCancellationRequested)
        {
            await PublishPendingAsync(stoppingToken);
            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    private async Task PublishPendingAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TContext>();
            var producer = scope.ServiceProvider.GetRequiredService<IKafkaProducer>();

            var messages = await db.OutboxMessages
                .Where(message => message.ProcessedAtUtc == null)
                .OrderBy(message => message.CreatedAtUtc)
                .Take(_options.OutboxBatchSize)
                .ToListAsync(cancellationToken);

            foreach (var message in messages)
            {
                try
                {
                    await producer.ProduceAsync(message.Topic, message.Key, message.Payload, cancellationToken);
                    message.ProcessedAtUtc = DateTimeOffset.UtcNow;
                    message.LastError = null;
                }
                catch (Exception ex)
                {
                    message.Attempts++;
                    message.LastError = ex.Message;
                    logger.LogWarning(ex, "Failed to publish outbox message {OutboxMessageId}", message.Id);
                }
            }

            if (messages.Count > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Outbox polling failed for {DbContext}", typeof(TContext).Name);
        }
    }
}
