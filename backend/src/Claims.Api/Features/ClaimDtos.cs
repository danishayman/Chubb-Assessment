using Claims.Domain;

namespace Claims.Api.Features;

// Requests
public record SubmitClaimRequest(Guid PolicyId, DateOnly IncidentDate, string Location, string Description, decimal ClaimedAmount);
public record AssignRequest(Guid? OfficerId);
public record ReserveRequest(decimal Amount);
public record InfoRequestRequest(string Question);
public record InfoResponseRequest(string Response);
public record ApproveRequest(decimal Amount, string? Reason);
public record RejectRequest(string Reason);
public record NoteRequest(string Text);

// Responses
public record PolicyDto(Guid Id, string PolicyNumber, ClaimType Type, Market Market, string Currency,
    decimal CoverageLimit, decimal Excess, DateOnly StartDate, DateOnly EndDate);

public record ClaimSummaryDto(
    Guid Id, string ClaimNumber, ClaimType Type, Market Market, string Currency, ClaimStatus Status,
    decimal ClaimedAmount, decimal? ReserveAmount, decimal? ApprovedAmount,
    DateOnly IncidentDate, DateTime SubmittedAt, DateTime UpdatedAt, int AgeDays,
    Guid? AssignedOfficerId, string? AssignedOfficerName, string ClaimantName, int OpenInfoRequests);

public record InfoRequestDto(Guid Id, string Question, DateTime RequestedAt, string? Response, DateTime? RespondedAt, InfoRequestStatus Status);
public record NoteDto(Guid Id, string AuthorName, string Text, DateTime CreatedAt);
public record ActivityDto(DateTime At, string Actor, string Action, string Detail);

public record ClaimDetailDto(
    ClaimSummaryDto Summary,
    string PolicyNumber, string Location, string Description,
    decimal? SettledAmount, string? DecisionReason, DateTime? ClosedAt,
    decimal CoverageLimit, decimal Excess, decimal? MaxPayable,
    IReadOnlyList<ClaimStatus> AllowedNextStatuses,
    IReadOnlyList<InfoRequestDto> InformationRequests,
    IReadOnlyList<NoteDto>? Notes,
    IReadOnlyList<ActivityDto> Timeline);

public record StaffDto(Guid Id, string Name, UserRole Role);
