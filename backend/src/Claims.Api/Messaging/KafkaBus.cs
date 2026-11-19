using System.Text;
using Claims.Api.Data;
using Confluent.Kafka;

namespace Claims.Api.Messaging;

/// <summary>
/// All claim events go to one topic keyed by claim id. Kafka only orders within a partition,
/// so this keeps each claim's events in order while still spreading claims across partitions.
/// </summary>
public sealed class KafkaPublisher : IEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;

    public KafkaPublisher(MessagingOptions options)
    {
        _topic = options.Kafka.Topic;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.Kafka.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = 10_000,
        }).Build();
    }

    public async Task PublishAsync(OutboxMessage message, CancellationToken ct)
    {
        await _producer.ProduceAsync(_topic, new Message<string, string>
        {
            Key = message.ClaimId.ToString(),
            Value = message.Payload,
            Headers = new Headers
            {
                { "event-type", Encoding.UTF8.GetBytes(message.Type) },
                { "event-id", Encoding.UTF8.GetBytes(message.EventId.ToString()) },
            },
        }, ct);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}

/// <summary>
/// Commits the offset only after handlers succeed (at-least-once). Handlers are idempotent,
/// so a redelivery after a crash is harmless.
/// </summary>
public class KafkaConsumer(MessagingOptions options, EventDispatcher dispatcher, ILogger<KafkaConsumer> log) : BackgroundService
{
    private const int MaxAttempts = 3;

    protected override Task ExecuteAsync(CancellationToken ct) => Task.Run(() => Run(ct), ct);

    private async Task Run(CancellationToken ct)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = options.Kafka.BootstrapServers,
            GroupId = options.Kafka.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();

        consumer.Subscribe(options.Kafka.Topic);
        log.LogInformation("Kafka consumer subscribed to {Topic}", options.Kafka.Topic);

        while (!ct.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result;
            try
            {
                result = consumer.Consume(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ConsumeException ex)
            {
                log.LogWarning("Kafka consume error: {Reason}", ex.Error.Reason);
                await Task.Delay(1000, ct);
                continue;
            }

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await dispatcher.DispatchAsync(result.Message.Value, ct);
                    break;
                }
                catch (Exception ex) when (attempt < MaxAttempts && !ct.IsCancellationRequested)
                {
                    log.LogWarning(ex, "Handler failed (attempt {Attempt}), retrying", attempt);
                    await Task.Delay(200 * attempt, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    // Next step would be a dead-letter topic. For now log it and move on so one bad event can't block the partition.
                    log.LogError(ex, "Giving up on event at {TopicPartitionOffset}", result.TopicPartitionOffset);
                    break;
                }
            }

            consumer.Commit(result);
        }

        consumer.Close();
    }
}
