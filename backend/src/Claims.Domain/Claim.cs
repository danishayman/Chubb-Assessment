using Claims.Domain.Events;

namespace Claims.Domain;

public class Claim
{
    private readonly List<InformationRequest> _infoRequests = [];
    private readonly List<ClaimNote> _notes = [];
    private readonly List<ClaimActivity> _activities = [];
    private readonly List<Attachment> _attachments = [];
    private readonly List<DomainEvent> _events = [];
    // One operation can log several entries with the same timestamp (e.g. "info provided" then "status changed").
    // Nudging each by a tick keeps them in the order they happened when read back.
    private int _logSequence;

    private Claim() { }

    public Guid Id { get; private set; }
    public string ClaimNumber { get; private set; } = "";
    public Guid PolicyId { get; private set; }
    public Guid ClaimantId { get; private set; }
    public ClaimType Type { get; private set; }
    public Market Market { get; private set; }
    public string Currency { get; private set; } = "";
    public DateOnly IncidentDate { get; private set; }
    public string Location { get; private set; } = "";
    public string Description { get; private set; } = "";

    public decimal ClaimedAmount { get; private set; }
    /// <summary>Current estimate of what we will pay. Drives outstanding liability exposure.</summary>
    public decimal ReserveAmount { get; private set; }
    public decimal? ApprovedAmount { get; private set; }
    public decimal? SettledAmount { get; private set; }
    public string? DecisionReason { get; private set; }

    public ClaimStatus Status { get; private set; }
    public Guid? AssignedOfficerId { get; private set; }
    public DateTime SubmittedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime? ClosedAt { get; private set; }

    /// <summary>Optimistic concurrency token, regenerated on every save.</summary>
    public Guid Version { get; set; }

    public IReadOnlyList<InformationRequest> InformationRequests => _infoRequests;
    public IReadOnlyList<ClaimNote> Notes => _notes;
    public IReadOnlyList<ClaimActivity> Activities => _activities;
    public IReadOnlyList<Attachment> Attachments => _attachments;
    public IReadOnlyList<DomainEvent> DomainEvents => _events;

    public bool IsOpen => ClaimWorkflow.IsOpen(Status);

    public static Claim Submit(Policy policy, Guid claimantId, DateOnly incidentDate, string location,
        string description, decimal claimedAmount, DateTime now)
    {
        if (policy.HolderId != claimantId)
            throw DomainException.Forbidden("Policy does not belong to this claimant.");
        if (incidentDate > DateOnly.FromDateTime(now))
            throw new DomainException("Incident date cannot be in the future.");
        if (!policy.Covers(incidentDate))
            throw new DomainException("Policy was not active on the incident date.");
        if (claimedAmount <= 0)
            throw new DomainException("Claimed amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("Description is required.");

        var claim = new Claim
        {
            Id = Guid.NewGuid(),
            ClaimNumber = $"CLM-{policy.Market}-{now:yyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            PolicyId = policy.Id,
            ClaimantId = claimantId,
            Type = policy.Type,
            Market = policy.Market,
            Currency = policy.Currency,
            IncidentDate = incidentDate,
            Location = location.Trim(),
            Description = description.Trim(),
            ClaimedAmount = claimedAmount,
            // Initial case reserve: what the policy would pay if the claim is valid as reported.
            // Gives managers an exposure figure from day one instead of waiting for an officer.
            ReserveAmount = policy.MaxPayable(claimedAmount),
            Status = ClaimStatus.Submitted,
            SubmittedAt = now,
            UpdatedAt = now,
        };

        claim.Log(claimantId, "Submitted", $"Claim submitted for {claim.Currency} {claimedAmount:N2}", true, now);
        claim.Raise(new ClaimSubmitted
        {
            ClaimId = claim.Id, ClaimNumber = claim.ClaimNumber, ClaimantId = claimantId, OccurredAt = now,
            Market = claim.Market, ClaimType = claim.Type, ClaimedAmount = claimedAmount, Currency = claim.Currency,
        });
        return claim;
    }

    /// <summary>Officer picks up the claim (or a manager assigns/reassigns it).</summary>
    public void Assign(Guid officerId, DateTime now)
    {
        EnsureOpen();
        if (AssignedOfficerId == officerId) return;

        AssignedOfficerId = officerId;
        Log(officerId, "Assigned", "Claim assigned to an officer", false, now);
        Raise(new ClaimAssigned { ClaimId = Id, ClaimNumber = ClaimNumber, ClaimantId = ClaimantId, OccurredAt = now, OfficerId = officerId });

        if (Status == ClaimStatus.Submitted)
            TransitionTo(ClaimStatus.UnderReview, officerId, null, now);
        else
            Touch(now);
    }

    public void SetReserve(Guid officerId, decimal amount, Policy policy, DateTime now, bool asManager = false)
    {
        EnsureCanWork(officerId, asManager);
        EnsureOpen();
        if (Status == ClaimStatus.Approved)
            throw DomainException.Conflict("Reserve is fixed once a claim is approved.");
        if (amount < 0 || amount > policy.CoverageLimit)
            throw new DomainException($"Reserve must be between 0 and the coverage limit ({policy.CoverageLimit:N2}).");
        if (amount == ReserveAmount) return;

        var old = ReserveAmount;
        ReserveAmount = amount;
        Log(officerId, "ReserveChanged", $"Reserve changed from {old:N2} to {amount:N2}", false, now);
        Raise(new ReserveChanged { ClaimId = Id, ClaimNumber = ClaimNumber, ClaimantId = ClaimantId, OccurredAt = now, OldAmount = old, NewAmount = amount });
        Touch(now);
    }

    public InformationRequest RequestInformation(Guid officerId, string question, DateTime now, bool asManager = false)
    {
        EnsureCanWork(officerId, asManager);
        if (string.IsNullOrWhiteSpace(question))
            throw new DomainException("Question is required.");
        if (Status is not (ClaimStatus.UnderReview or ClaimStatus.AwaitingInformation))
            throw DomainException.Conflict($"Cannot request information while claim is {Status}.");

        var request = new InformationRequest(Id, officerId, question.Trim(), now);
        _infoRequests.Add(request);
        Log(officerId, "InformationRequested", request.Question, true, now);
        Raise(new InformationRequested { ClaimId = Id, ClaimNumber = ClaimNumber, ClaimantId = ClaimantId, OccurredAt = now, RequestId = request.Id, Question = request.Question });

        if (Status == ClaimStatus.UnderReview)
            TransitionTo(ClaimStatus.AwaitingInformation, officerId, null, now);
        else
            Touch(now);
        return request;
    }

    public void ProvideInformation(Guid claimantId, Guid requestId, string response, DateTime now)
    {
        if (claimantId != ClaimantId)
            throw DomainException.Forbidden("Only the claimant can respond to information requests.");
        EnsureOpen();
        if (string.IsNullOrWhiteSpace(response))
            throw new DomainException("Response is required.");

        var request = _infoRequests.FirstOrDefault(r => r.Id == requestId)
            ?? throw new DomainException("Information request not found.");
        if (request.Status != InfoRequestStatus.Open)
            throw DomainException.Conflict("This request has already been answered.");

        request.Answer(response.Trim(), now);
        Log(claimantId, "InformationProvided", "Claimant responded to an information request", true, now);
        Raise(new InformationProvided { ClaimId = Id, ClaimNumber = ClaimNumber, ClaimantId = ClaimantId, OccurredAt = now, RequestId = requestId, OfficerId = AssignedOfficerId });

        var stillWaiting = _infoRequests.Any(r => r.Status == InfoRequestStatus.Open);
        if (Status == ClaimStatus.AwaitingInformation && !stillWaiting)
            TransitionTo(ClaimStatus.UnderReview, claimantId, null, now);
        else
            Touch(now);
    }

    public void Approve(Guid officerId, decimal amount, Policy policy, string? reason, DateTime now, bool asManager = false)
    {
        EnsureCanWork(officerId, asManager);
        var max = policy.MaxPayable(ClaimedAmount);
        if (amount <= 0 || amount > max)
            throw new DomainException($"Approved amount must be greater than 0 and at most {max:N2} (claimed amount less excess, capped at limit).");

        TransitionTo(ClaimStatus.Approved, officerId, reason, now);
        ApprovedAmount = amount;
        ReserveAmount = amount;
        DecisionReason = reason;
    }

    public void Reject(Guid officerId, string reason, DateTime now, bool asManager = false)
    {
        EnsureCanWork(officerId, asManager);
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A reason is required to reject a claim.");

        TransitionTo(ClaimStatus.Rejected, officerId, reason.Trim(), now);
        DecisionReason = reason.Trim();
        ReserveAmount = 0;
        ClosedAt = now;
    }

    public void Settle(Guid officerId, DateTime now, bool asManager = false)
    {
        EnsureCanWork(officerId, asManager);
        TransitionTo(ClaimStatus.Settled, officerId, null, now);
        SettledAmount = ApprovedAmount;
        ReserveAmount = 0;
        ClosedAt = now;
        Raise(new ClaimSettled { ClaimId = Id, ClaimNumber = ClaimNumber, ClaimantId = ClaimantId, OccurredAt = now, Amount = SettledAmount!.Value, Currency = Currency });
    }

    public const long MaxAttachmentBytes = 10 * 1024 * 1024;
    public const int MaxAttachmentsPerClaim = 20;

    /// <summary>
    /// Claimant adds supporting evidence, optionally in answer to a specific information request.
    /// The caller must load <see cref="Attachments"/> so the per-claim limit can be checked.
    /// </summary>
    public Attachment AddAttachment(Guid claimantId, string fileName, string contentType, long sizeBytes,
        Guid? informationRequestId, DateTime now)
    {
        if (claimantId != ClaimantId)
            throw DomainException.Forbidden("Only the claimant can add documents to this claim.");
        EnsureOpen();
        if (sizeBytes <= 0)
            throw new DomainException("The file is empty.");
        if (sizeBytes > MaxAttachmentBytes)
            throw new DomainException($"Files must be {MaxAttachmentBytes / 1024 / 1024} MB or smaller.");
        if (_attachments.Count >= MaxAttachmentsPerClaim)
            throw new DomainException($"A claim can have at most {MaxAttachmentsPerClaim} documents.");
        if (informationRequestId is { } rid && _infoRequests.All(r => r.Id != rid))
            throw new DomainException("Information request not found.");

        var attachment = new Attachment(Id, informationRequestId, claimantId, fileName, contentType, sizeBytes, now);
        _attachments.Add(attachment);
        Log(claimantId, "AttachmentAdded", $"Document added: {fileName}", true, now);
        Raise(new AttachmentAdded
        {
            ClaimId = Id, ClaimNumber = ClaimNumber, ClaimantId = ClaimantId, OccurredAt = now,
            AttachmentId = attachment.Id, FileName = fileName, OfficerId = AssignedOfficerId,
        });
        Touch(now);
        return attachment;
    }

    public ClaimNote AddNote(Guid authorId, string text, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new DomainException("Note text is required.");
        var note = new ClaimNote(Id, authorId, text.Trim(), now);
        _notes.Add(note);
        Touch(now);
        return note;
    }

    public void ClearDomainEvents() => _events.Clear();

    private void TransitionTo(ClaimStatus to, Guid actorId, string? reason, DateTime now)
    {
        if (!ClaimWorkflow.CanTransition(Status, to))
            throw DomainException.Conflict($"Cannot move claim from {Status} to {to}.");

        var from = Status;
        Status = to;
        Touch(now);
        Log(actorId, "StatusChanged", reason is null ? $"{from} → {to}" : $"{from} → {to}: {reason}", true, now);
        Raise(new ClaimStatusChanged { ClaimId = Id, ClaimNumber = ClaimNumber, ClaimantId = ClaimantId, OccurredAt = now, From = from, To = to, Reason = reason });
    }

    /// <summary>
    /// The assigned officer works the claim. A manager can also act on it directly (cover for absence,
    /// escalations) without taking it over, so the claim keeps its owner. The audit trail records who acted.
    /// Every claim still needs an owner first, so managers can't skip the pick-up step.
    /// </summary>
    private void EnsureCanWork(Guid actorId, bool asManager)
    {
        if (AssignedOfficerId is null)
            throw DomainException.Conflict("Claim must be assigned before it can be worked on.");
        if (AssignedOfficerId != actorId && !asManager)
            throw DomainException.Forbidden("Claim is assigned to another officer.");
    }

    private void EnsureOpen()
    {
        if (!IsOpen) throw DomainException.Conflict($"Claim is closed ({Status}).");
    }

    private void Touch(DateTime now) => UpdatedAt = now;

    private void Log(Guid actorId, string action, string detail, bool visibleToClaimant, DateTime now) =>
        _activities.Add(new ClaimActivity(Id, actorId, action, detail, visibleToClaimant, now.AddTicks(_logSequence++)));

    private void Raise(DomainEvent e) => _events.Add(e);
}
