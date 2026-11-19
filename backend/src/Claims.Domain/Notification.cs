namespace Claims.Domain;

public class Notification
{
    private Notification() { }

    public Notification(Guid userId, Guid claimId, string message, Guid sourceEventId, DateTime now)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        ClaimId = claimId;
        Message = message;
        SourceEventId = sourceEventId;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid ClaimId { get; private set; }
    public string Message { get; private set; } = "";
    /// <summary>Event that produced this notification. Unique per user, so redelivered events are ignored.</summary>
    public Guid SourceEventId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ReadAt { get; private set; }

    public void MarkRead(DateTime now) => ReadAt ??= now;
}
