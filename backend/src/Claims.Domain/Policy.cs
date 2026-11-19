namespace Claims.Domain;

public class Policy
{
    private Policy() { }

    public Policy(Guid id, string policyNumber, Guid holderId, ClaimType type, Market market,
        string currency, decimal coverageLimit, decimal excess, DateOnly startDate, DateOnly endDate)
    {
        Id = id;
        PolicyNumber = policyNumber;
        HolderId = holderId;
        Type = type;
        Market = market;
        Currency = currency;
        CoverageLimit = coverageLimit;
        Excess = excess;
        StartDate = startDate;
        EndDate = endDate;
    }

    public Guid Id { get; private set; }
    public string PolicyNumber { get; private set; } = "";
    public Guid HolderId { get; private set; }
    public ClaimType Type { get; private set; }
    public Market Market { get; private set; }
    public string Currency { get; private set; } = "";
    public decimal CoverageLimit { get; private set; }
    public decimal Excess { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }

    public bool Covers(DateOnly date) => date >= StartDate && date <= EndDate;

    /// <summary>What the policy would pay for a given loss: loss minus excess, capped at the limit.</summary>
    public decimal MaxPayable(decimal lossAmount) =>
        Math.Clamp(lossAmount - Excess, 0m, CoverageLimit);
}
