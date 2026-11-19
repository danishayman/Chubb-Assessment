using Claims.Domain;

namespace Claims.Tests.Domain;

public class ExposureTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Claimant = Guid.NewGuid();
    private static readonly Guid Officer = Guid.NewGuid();

    private static Policy Policy(Market market, string currency, ClaimType type) =>
        new(Guid.NewGuid(), "P", Claimant, type, market, currency, 100_000m, 0m,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

    private static Claim Submit(Policy p, decimal amount) =>
        Claim.Submit(p, Claimant, new DateOnly(2026, 10, 1), "x", "y", amount, Now);

    [Fact]
    public void Sums_reserves_of_open_claims_per_market_and_type()
    {
        var sgMotor = Policy(Market.SG, "SGD", ClaimType.Motor);
        var myProp = Policy(Market.MY, "MYR", ClaimType.Property);

        var settled = Submit(sgMotor, 3_000m);
        settled.Assign(Officer, Now);
        settled.Approve(Officer, 3_000m, sgMotor, null, Now);
        settled.Settle(Officer, Now);

        var claims = new[] { Submit(sgMotor, 1_000m), Submit(sgMotor, 2_000m), Submit(myProp, 5_000m), settled };

        var lines = Exposure.Calculate(claims);

        Assert.Equal(2, lines.Count);
        var sg = lines.Single(l => l.Market == Market.SG);
        Assert.Equal(2, sg.OpenClaims);
        Assert.Equal(3_000m, sg.Reserved);
        Assert.Equal("SGD", sg.Currency);
        Assert.Equal(5_000m, lines.Single(l => l.Market == Market.MY).Reserved);
    }
}
