using System.Text.Json.Serialization;

namespace Claims.Domain.Events;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ClaimSubmitted), "claim.submitted")]
[JsonDerivedType(typeof(ClaimAssigned), "claim.assigned")]
[JsonDerivedType(typeof(ClaimStatusChanged), "claim.status-changed")]
[JsonDerivedType(typeof(InformationRequested), "claim.info-requested")]
[JsonDerivedType(typeof(InformationProvided), "claim.info-provided")]
[JsonDerivedType(typeof(ReserveChanged), "claim.reserve-changed")]
[JsonDerivedType(typeof(ClaimSettled), "claim.settled")]
public abstract record DomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public required Guid ClaimId { get; init; }
    public required string ClaimNumber { get; init; }
    public required Guid ClaimantId { get; init; }
    public required DateTime OccurredAt { get; init; }
}

public record ClaimSubmitted : DomainEvent
{
    public required Market Market { get; init; }
    public required ClaimType ClaimType { get; init; }
    public required decimal ClaimedAmount { get; init; }
    public required string Currency { get; init; }
}

public record ClaimAssigned : DomainEvent
{
    public required Guid OfficerId { get; init; }
}

public record ClaimStatusChanged : DomainEvent
{
    public required ClaimStatus From { get; init; }
    public required ClaimStatus To { get; init; }
    public string? Reason { get; init; }
}

public record InformationRequested : DomainEvent
{
    public required Guid RequestId { get; init; }
    public required string Question { get; init; }
}

public record InformationProvided : DomainEvent
{
    public required Guid RequestId { get; init; }
    public Guid? OfficerId { get; init; }
}

public record ReserveChanged : DomainEvent
{
    public required decimal OldAmount { get; init; }
    public required decimal NewAmount { get; init; }
}

public record ClaimSettled : DomainEvent
{
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
}
