using Claims.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Messaging;

/// <summary>
/// Moves events from the outbox table to the broker in insert order.
/// Stops at the first failure so events for a claim are never published out of order.
/// </summary>
public class OutboxDispatcher(IServiceScopeFactory scopes, IEventPublisher publisher, MessagingOptions options,
    TimeProvider clock, ILogger<OutboxDispatcher> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                log.LogError(ex, "Outbox dispatch failed");
            }

            try { await Task.Delay(options.OutboxPollMs, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task DispatchBatchAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();

        var batch = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.Id)
            .Take(50)
            .ToListAsync(ct);

        foreach (var message in batch)
        {
            try
            {
                await publisher.PublishAsync(message, ct);
                message.ProcessedAt = clock.GetUtcNow().UtcDateTime;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                message.Attempts++;
                message.LastError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                log.LogWarning(ex, "Could not publish outbox message {Id} ({Type}), will retry", message.Id, message.Type);
                break;
            }
        }

        if (batch.Count > 0) await db.SaveChangesAsync(ct);
    }
}
