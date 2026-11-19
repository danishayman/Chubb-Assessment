using System.Text.Json;
using Claims.Api.Data;
using Claims.Domain.Events;

namespace Claims.Api.Messaging;

/// <summary>Publishes an outbox message to the broker. Must only return once the broker has accepted it.</summary>
public interface IEventPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken ct);
}

/// <summary>Reacts to a domain event. Must be idempotent: delivery is at-least-once.</summary>
public interface IEventHandler
{
    Task HandleAsync(DomainEvent e, CancellationToken ct);
}

public class MessagingOptions
{
    public string Provider { get; set; } = "InMemory";
    public int OutboxPollMs { get; set; } = 500;
    public KafkaOptions Kafka { get; set; } = new();
}

public class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string Topic { get; set; } = "claims.events";
    public string GroupId { get; set; } = "claims-api";
}

/// <summary>Runs every registered handler for an event in its own DI scope.</summary>
public class EventDispatcher(IServiceScopeFactory scopes, ILogger<EventDispatcher> log)
{
    public async Task DispatchAsync(string payload, CancellationToken ct)
    {
        var e = JsonSerializer.Deserialize<DomainEvent>(payload, ClaimsDbContext.EventJson)
            ?? throw new InvalidOperationException("Empty event payload.");

        using var scope = scopes.CreateScope();
        foreach (var handler in scope.ServiceProvider.GetServices<IEventHandler>())
        {
            await handler.HandleAsync(e, ct);
        }
        log.LogDebug("Handled {Type} {EventId} for {ClaimNumber}", e.GetType().Name, e.EventId, e.ClaimNumber);
    }
}
