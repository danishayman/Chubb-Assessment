namespace Claims.Domain;

/// <summary>Audit trail entry. Written in the same transaction as the change it describes.</summary>
public class ClaimActivity
{
    private ClaimActivity() { }

    internal ClaimActivity(Guid claimId, Guid actorId, string action, string detail, bool visibleToClaimant, DateTime now)
    {
        Id = Guid.NewGuid();
        ClaimId = claimId;
        ActorId = actorId;
        Action = action;
        Detail = detail;
        VisibleToClaimant = visibleToClaimant;
        At = now;
    }

    public Guid Id { get; private set; }
    public Guid ClaimId { get; private set; }
    public Guid ActorId { get; private set; }
    public string Action { get; private set; } = "";
    public string Detail { get; private set; } = "";
    public bool VisibleToClaimant { get; private set; }
    public DateTime At { get; private set; }
}
