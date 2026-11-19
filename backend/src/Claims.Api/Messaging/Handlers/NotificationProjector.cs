using Claims.Api.Data;
using Claims.Domain;
using Claims.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Messaging.Handlers;

/// <summary>Turns claim events into in-app notifications for claimants and officers.</summary>
public class NotificationProjector(ClaimsDbContext db) : IEventHandler
{
    public async Task HandleAsync(DomainEvent e, CancellationToken ct)
    {
        var (userId, message) = e switch
        {
            ClaimSubmitted s => (s.ClaimantId, $"We've received your claim {s.ClaimNumber}. We'll let you know when an officer picks it up."),
            ClaimStatusChanged { From: ClaimStatus.Submitted, To: ClaimStatus.UnderReview } s =>
                (s.ClaimantId, $"Your claim {s.ClaimNumber} is now being reviewed by a claims officer."),
            ClaimStatusChanged { To: ClaimStatus.Approved } s =>
                (s.ClaimantId, $"Good news: your claim {s.ClaimNumber} has been approved."),
            ClaimStatusChanged { To: ClaimStatus.Rejected } s =>
                (s.ClaimantId, $"Your claim {s.ClaimNumber} was not approved. Reason: {s.Reason}"),
            ClaimSettled s => (s.ClaimantId, $"Your claim {s.ClaimNumber} has been settled. Payment of {s.Currency} {s.Amount:N2} is on its way."),
            InformationRequested s => (s.ClaimantId, $"We need more information for claim {s.ClaimNumber}: {s.Question}"),
            InformationProvided { OfficerId: { } officer } s => (officer, $"The claimant responded to your request on {s.ClaimNumber}."),
            _ => (Guid.Empty, ""),
        };
        if (userId == Guid.Empty) return;

        // Delivery is at-least-once, so skip events we've already turned into a notification.
        if (await db.Notifications.AnyAsync(n => n.UserId == userId && n.SourceEventId == e.EventId, ct)) return;

        db.Notifications.Add(new Notification(userId, e.ClaimId, message, e.EventId, e.OccurredAt));
        await db.SaveChangesAsync(ct);
    }
}
