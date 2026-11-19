namespace Claims.Domain;

public record ExposureLine(Market Market, string Currency, ClaimType Type, int OpenClaims, decimal Reserved, decimal Claimed);

public static class Exposure
{
    /// <summary>
    /// Outstanding liability = sum of reserves on open claims. Grouped by market (and so by currency):
    /// six markets means six currencies, and adding them up without FX rates would give a meaningless number.
    /// </summary>
    public static IReadOnlyList<ExposureLine> Calculate(IEnumerable<Claim> claims) =>
        claims
            .Where(c => c.IsOpen)
            .GroupBy(c => (c.Market, c.Currency, c.Type))
            .Select(g => new ExposureLine(g.Key.Market, g.Key.Currency, g.Key.Type,
                g.Count(), g.Sum(c => c.ReserveAmount), g.Sum(c => c.ClaimedAmount)))
            .OrderBy(l => l.Market).ThenBy(l => l.Type)
            .ToList();
}
