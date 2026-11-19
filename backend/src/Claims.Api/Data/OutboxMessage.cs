namespace Claims.Api.Data;

/// <summary>
/// Domain event waiting to be published. Written in the same transaction as the claim change,
/// so an event is never lost if the broker is down, and never published for a change that rolled back.
/// </summary>
public class OutboxMessage
{
    public long Id { get; set; }
    public Guid EventId { get; set; }
    public Guid ClaimId { get; set; }
    public string Type { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTime OccurredAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
