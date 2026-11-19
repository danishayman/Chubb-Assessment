namespace Claims.Domain;

public static class ClaimWorkflow
{
    private static readonly Dictionary<ClaimStatus, ClaimStatus[]> Allowed = new()
    {
        [ClaimStatus.Submitted] = [ClaimStatus.UnderReview],
        [ClaimStatus.UnderReview] = [ClaimStatus.AwaitingInformation, ClaimStatus.Approved, ClaimStatus.Rejected],
        [ClaimStatus.AwaitingInformation] = [ClaimStatus.UnderReview, ClaimStatus.Rejected],
        [ClaimStatus.Approved] = [ClaimStatus.Settled],
        [ClaimStatus.Rejected] = [],
        [ClaimStatus.Settled] = [],
    };

    public static bool CanTransition(ClaimStatus from, ClaimStatus to) => Allowed[from].Contains(to);

    public static bool IsOpen(ClaimStatus status) => status is not (ClaimStatus.Rejected or ClaimStatus.Settled);

    public static IReadOnlyList<ClaimStatus> OpenStatuses { get; } =
        Enum.GetValues<ClaimStatus>().Where(IsOpen).ToArray();
}
