using Claims.Domain;
using Claims.Domain.Events;

namespace Claims.Tests.Domain;

public class ClaimTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Claimant = Guid.NewGuid();
    private static readonly Guid Officer = Guid.NewGuid();

    private static Policy MotorPolicy(decimal limit = 50_000m, decimal excess = 500m) =>
        new(Guid.NewGuid(), "POL-SG-1", Claimant, ClaimType.Motor, Market.SG, "SGD", limit, excess,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

    private static Claim NewClaim(Policy? policy = null, decimal amount = 8_000m) =>
        Claim.Submit(policy ?? MotorPolicy(), Claimant, new DateOnly(2026, 10, 28), "Orchard Rd", "Rear-ended at lights", amount, Now);

    private static Claim AssignedClaim(Policy? policy = null)
    {
        var claim = NewClaim(policy);
        claim.Assign(Officer, Now);
        return claim;
    }

    [Fact]
    public void Submit_sets_initial_reserve_to_claimed_amount_less_excess()
    {
        var claim = NewClaim(MotorPolicy(excess: 500m), amount: 8_000m);

        Assert.Equal(ClaimStatus.Submitted, claim.Status);
        Assert.Equal(7_500m, claim.ReserveAmount);
        Assert.Single(claim.DomainEvents.OfType<ClaimSubmitted>());
    }

    [Fact]
    public void Submit_caps_initial_reserve_at_coverage_limit()
    {
        var claim = NewClaim(MotorPolicy(limit: 5_000m, excess: 0m), amount: 20_000m);
        Assert.Equal(5_000m, claim.ReserveAmount);
    }

    [Fact]
    public void Submit_rejects_incident_outside_policy_period()
    {
        var ex = Assert.Throws<DomainException>(() =>
            Claim.Submit(MotorPolicy(), Claimant, new DateOnly(2025, 6, 1), "x", "y", 100m, Now));
        Assert.Equal(DomainErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public void Submit_rejects_someone_elses_policy()
    {
        var ex = Assert.Throws<DomainException>(() =>
            Claim.Submit(MotorPolicy(), Guid.NewGuid(), new DateOnly(2026, 10, 1), "x", "y", 100m, Now));
        Assert.Equal(DomainErrorKind.Forbidden, ex.Kind);
    }

    [Fact]
    public void Assign_moves_submitted_claim_into_review()
    {
        var claim = AssignedClaim();

        Assert.Equal(ClaimStatus.UnderReview, claim.Status);
        Assert.Equal(Officer, claim.AssignedOfficerId);
        Assert.Contains(claim.DomainEvents.OfType<ClaimStatusChanged>(), e => e.To == ClaimStatus.UnderReview);
    }

    [Fact]
    public void Cannot_approve_unassigned_claim()
    {
        var policy = MotorPolicy();
        var claim = NewClaim(policy);
        var ex = Assert.Throws<DomainException>(() => claim.Approve(Officer, 100m, policy, null, Now));
        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
    }

    [Fact]
    public void Other_officer_cannot_act_on_claim()
    {
        var policy = MotorPolicy();
        var claim = AssignedClaim(policy);
        var ex = Assert.Throws<DomainException>(() => claim.Approve(Guid.NewGuid(), 100m, policy, null, Now));
        Assert.Equal(DomainErrorKind.Forbidden, ex.Kind);
    }

    [Fact]
    public void Information_round_trip_returns_claim_to_review_only_when_all_answered()
    {
        var claim = AssignedClaim();
        var q1 = claim.RequestInformation(Officer, "Police report?", Now);
        var q2 = claim.RequestInformation(Officer, "Photos of damage?", Now);
        Assert.Equal(ClaimStatus.AwaitingInformation, claim.Status);

        claim.ProvideInformation(Claimant, q1.Id, "Attached", Now);
        Assert.Equal(ClaimStatus.AwaitingInformation, claim.Status);

        claim.ProvideInformation(Claimant, q2.Id, "Attached", Now);
        Assert.Equal(ClaimStatus.UnderReview, claim.Status);
    }

    [Fact]
    public void Cannot_answer_the_same_request_twice()
    {
        var claim = AssignedClaim();
        var q = claim.RequestInformation(Officer, "Police report?", Now);
        claim.ProvideInformation(Claimant, q.Id, "Attached", Now);

        Assert.Throws<DomainException>(() => claim.ProvideInformation(Claimant, q.Id, "Again", Now));
    }

    [Fact]
    public void Cannot_approve_while_awaiting_information()
    {
        var policy = MotorPolicy();
        var claim = AssignedClaim(policy);
        claim.RequestInformation(Officer, "Police report?", Now);

        var ex = Assert.Throws<DomainException>(() => claim.Approve(Officer, 1_000m, policy, null, Now));
        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
    }

    [Fact]
    public void Approve_cannot_exceed_max_payable()
    {
        var policy = MotorPolicy(excess: 500m);
        var claim = AssignedClaim(policy); // claimed 8,000 -> max 7,500

        Assert.Throws<DomainException>(() => claim.Approve(Officer, 7_600m, policy, null, Now));
    }

    [Fact]
    public void Approve_then_settle_releases_reserve()
    {
        var policy = MotorPolicy();
        var claim = AssignedClaim(policy);

        claim.Approve(Officer, 6_000m, policy, "Liability accepted", Now);
        Assert.Equal(6_000m, claim.ReserveAmount);

        claim.Settle(Officer, Now);
        Assert.Equal(ClaimStatus.Settled, claim.Status);
        Assert.Equal(6_000m, claim.SettledAmount);
        Assert.Equal(0m, claim.ReserveAmount);
        Assert.False(claim.IsOpen);
        Assert.Single(claim.DomainEvents.OfType<ClaimSettled>());
    }

    [Fact]
    public void Cannot_settle_before_approval()
    {
        var claim = AssignedClaim();
        var ex = Assert.Throws<DomainException>(() => claim.Settle(Officer, Now));
        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
    }

    [Fact]
    public void Reject_requires_reason_and_closes_claim()
    {
        var claim = AssignedClaim();
        Assert.Throws<DomainException>(() => claim.Reject(Officer, " ", Now));

        claim.Reject(Officer, "Not covered: racing", Now);
        Assert.Equal(ClaimStatus.Rejected, claim.Status);
        Assert.Equal(0m, claim.ReserveAmount);
        Assert.Throws<DomainException>(() => claim.Assign(Guid.NewGuid(), Now));
    }

    [Fact]
    public void Claim_awaiting_information_can_be_rejected()
    {
        var claim = AssignedClaim();
        claim.RequestInformation(Officer, "Police report?", Now);

        claim.Reject(Officer, "No response from claimant", Now);
        Assert.Equal(ClaimStatus.Rejected, claim.Status);
    }

    [Fact]
    public void Reserve_must_stay_within_coverage_limit()
    {
        var policy = MotorPolicy(limit: 10_000m);
        var claim = AssignedClaim(policy);

        Assert.Throws<DomainException>(() => claim.SetReserve(Officer, 10_001m, policy, Now));
        claim.SetReserve(Officer, 9_000m, policy, Now);
        Assert.Equal(9_000m, claim.ReserveAmount);
    }

    [Theory]
    [InlineData(ClaimStatus.Submitted, ClaimStatus.Approved)]
    [InlineData(ClaimStatus.Submitted, ClaimStatus.Settled)]
    [InlineData(ClaimStatus.Rejected, ClaimStatus.UnderReview)]
    [InlineData(ClaimStatus.Settled, ClaimStatus.UnderReview)]
    [InlineData(ClaimStatus.Approved, ClaimStatus.Rejected)]
    public void Workflow_blocks_invalid_transitions(ClaimStatus from, ClaimStatus to) =>
        Assert.False(ClaimWorkflow.CanTransition(from, to));
}
