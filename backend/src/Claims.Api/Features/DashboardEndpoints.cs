using Claims.Api.Auth;
using Claims.Api.Data;
using Claims.Domain;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Features;

public record StatusCount(ClaimStatus Status, int Count);
public record AgeBucket(string Label, int Count);
public record OfficerWorkload(Guid OfficerId, string Name, int Open, int AwaitingInformation, int OldestOpenDays,
    int ClosedLast30Days, double? AvgDaysToClose);
public record WorkloadDto(int OpenClaims, int Unassigned, int OldestUnassignedDays,
    IReadOnlyList<StatusCount> ByStatus, IReadOnlyList<AgeBucket> Ageing, IReadOnlyList<OfficerWorkload> Officers);

public record LargeClaim(Guid Id, string ClaimNumber, Market Market, string Currency, ClaimStatus Status, decimal ReserveAmount);
public record ExposureDto(IReadOnlyList<ExposureLine> Lines, IReadOnlyList<LargeClaim> LargestOpenByMarket);

/// <summary>
/// Team-level views for staff. These aggregate in memory over open claims, which is fine at this size.
/// At real volume they'd come from SQL aggregates or a read model fed by the same events.
/// </summary>
public static class DashboardEndpoints
{
    public static void MapDashboard(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/dashboard").WithTags("Dashboard").RequireAuthorization(Policies.Staff);

        g.MapGet("/workload", async (ClaimsDbContext db, TimeProvider clock) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var since = now.AddDays(-30);

            var open = await db.Claims.AsNoTracking()
                .Where(c => ClaimWorkflow.OpenStatuses.Contains(c.Status)).ToListAsync();
            var recentlyClosed = await db.Claims.AsNoTracking()
                .Where(c => c.ClosedAt != null && c.ClosedAt >= since).ToListAsync();
            var officers = await db.Users.AsNoTracking()
                .Where(u => u.Role == UserRole.ClaimsOfficer || u.Role == UserRole.Manager).ToListAsync();

            int Age(Claim c) => (int)(now - c.SubmittedAt).TotalDays;
            var unassigned = open.Where(c => c.AssignedOfficerId == null).ToList();

            var byOfficer = officers
                .Select(o =>
                {
                    var mine = open.Where(c => c.AssignedOfficerId == o.Id).ToList();
                    var closed = recentlyClosed.Where(c => c.AssignedOfficerId == o.Id).ToList();
                    return new OfficerWorkload(o.Id, o.Name, mine.Count,
                        mine.Count(c => c.Status == ClaimStatus.AwaitingInformation),
                        mine.Count == 0 ? 0 : mine.Max(Age),
                        closed.Count,
                        closed.Count == 0 ? null : Math.Round(closed.Average(c => (c.ClosedAt!.Value - c.SubmittedAt).TotalDays), 1));
                })
                // Managers only show up if they are actually carrying claims.
                .Where(o => o.Open > 0 || o.ClosedLast30Days > 0 || officers.Any(u => u.Id == o.OfficerId && u.Role == UserRole.ClaimsOfficer))
                .OrderByDescending(o => o.Open)
                .ToList();

            AgeBucket Bucket(string label, Func<int, bool> match) => new(label, open.Count(c => match(Age(c))));

            return new WorkloadDto(
                open.Count, unassigned.Count, unassigned.Count == 0 ? 0 : unassigned.Max(Age),
                ClaimWorkflow.OpenStatuses.Select(s => new StatusCount(s, open.Count(c => c.Status == s))).ToList(),
                [Bucket("0-7 days", d => d <= 7), Bucket("8-14 days", d => d is > 7 and <= 14),
                 Bucket("15-30 days", d => d is > 14 and <= 30), Bucket("30+ days", d => d > 30)],
                byOfficer);
        });

        g.MapGet("/exposure", async (ClaimsDbContext db) =>
        {
            var open = await db.Claims.AsNoTracking()
                .Where(c => ClaimWorkflow.OpenStatuses.Contains(c.Status)).ToListAsync();

            return new ExposureDto(
                Exposure.Calculate(open),
                // Per market: comparing raw amounts across currencies would rank an IDR claim above an SGD one.
                open.GroupBy(c => c.Market)
                    .SelectMany(m => m.OrderByDescending(c => c.ReserveAmount).Take(3))
                    .OrderBy(c => c.Market)
                    .Select(c => new LargeClaim(c.Id, c.ClaimNumber, c.Market, c.Currency, c.Status, c.ReserveAmount))
                    .ToList());
        });
    }
}
