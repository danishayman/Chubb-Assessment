namespace Claims.Domain;

/// <summary>Internal staff note. Never shown to the claimant.</summary>
public class ClaimNote
{
    private ClaimNote() { }

    internal ClaimNote(Guid claimId, Guid authorId, string text, DateTime now)
    {
        Id = Guid.NewGuid();
        ClaimId = claimId;
        AuthorId = authorId;
        Text = text;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ClaimId { get; private set; }
    public Guid AuthorId { get; private set; }
    public string Text { get; private set; } = "";
    public DateTime CreatedAt { get; private set; }
}
