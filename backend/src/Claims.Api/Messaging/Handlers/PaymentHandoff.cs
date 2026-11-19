using Claims.Domain.Events;

namespace Claims.Api.Messaging.Handlers;

/// <summary>
/// Placeholder for the finance/payments integration. In production this would be a separate
/// service on its own consumer group, which is exactly why settlement goes out as an event.
/// </summary>
public class PaymentHandoff(ILogger<PaymentHandoff> log) : IEventHandler
{
    public Task HandleAsync(DomainEvent e, CancellationToken ct)
    {
        if (e is ClaimSettled s)
            log.LogInformation("Payment instruction: {Currency} {Amount} for claim {ClaimNumber}", s.Currency, s.Amount, s.ClaimNumber);
        return Task.CompletedTask;
    }
}
