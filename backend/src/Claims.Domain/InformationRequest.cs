namespace Claims.Domain;

public class InformationRequest
{
    private InformationRequest() { }

    internal InformationRequest(Guid claimId, Guid requestedBy, string question, DateTime now)
    {
        Id = Guid.NewGuid();
        ClaimId = claimId;
        RequestedBy = requestedBy;
        Question = question;
        RequestedAt = now;
        Status = InfoRequestStatus.Open;
    }

    public Guid Id { get; private set; }
    public Guid ClaimId { get; private set; }
    public Guid RequestedBy { get; private set; }
    public string Question { get; private set; } = "";
    public DateTime RequestedAt { get; private set; }
    public string? Response { get; private set; }
    public DateTime? RespondedAt { get; private set; }
    public InfoRequestStatus Status { get; private set; }

    internal void Answer(string response, DateTime now)
    {
        Response = response;
        RespondedAt = now;
        Status = InfoRequestStatus.Answered;
    }
}
