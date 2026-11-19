using System.Threading.Channels;
using Claims.Api.Data;

namespace Claims.Api.Messaging;

/// <summary>
/// In-process stand-in for Kafka so the app runs with no infrastructure.
/// Same contract as the Kafka publisher, but nothing survives a restart once it leaves the outbox.
/// </summary>
public class InMemoryBus : IEventPublisher
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<string> Reader => _channel.Reader;

    public Task PublishAsync(OutboxMessage message, CancellationToken ct) =>
        _channel.Writer.WriteAsync(message.Payload, ct).AsTask();
}

public class InMemoryConsumer(InMemoryBus bus, EventDispatcher dispatcher, ILogger<InMemoryConsumer> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var payload in bus.Reader.ReadAllAsync(ct))
            {
                try
                {
                    await dispatcher.DispatchAsync(payload, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    log.LogError(ex, "Event handler failed");
                }
            }
        }
        catch (OperationCanceledException) { }
    }
}
