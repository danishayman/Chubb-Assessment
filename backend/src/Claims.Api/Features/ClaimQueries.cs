using System.Security.Claims;
using Claims.Api.Auth;
using Claims.Api.Data;
using Claims.Domain;
using Microsoft.EntityFrameworkCore;
using Claim = Claims.Domain.Claim;

namespace Claims.Api.Features;

/// <summary>
/// Read side for claims. Claimants and staff get the same shape, but claimants never see
/// reserves, internal notes or internal timeline entries.
/// </summary>
public static class ClaimQueries
{
    public static async Task<List<ClaimSummaryDto>> Summaries(ClaimsDbContext db, IQueryable<Claim> claims,
        ClaimsPrincipal user, TimeProvider clock)
    {
        var rows = await (
            from c in claims
            join claimant in db.Users on c.ClaimantId equals claimant.Id
            join o in db.Users on c.AssignedOfficerId equals o.Id into officers
            from officer in officers.DefaultIfEmpty()
            select new
            {
                Claim = c,
                ClaimantName = claimant.Name,
                OfficerName = officer != null ? officer.Name : null,
                OpenInfo = db.InformationRequests.Count(r => r.ClaimId == c.Id && r.Status == InfoRequestStatus.Open),
            }).ToListAsync();

        var now = clock.GetUtcNow().UtcDateTime;
        var staff = user.IsStaff();
        return rows
            .OrderBy(r => r.Claim.SubmittedAt)
            .Select(r => ToSummary(r.Claim, r.ClaimantName, r.OfficerName, r.OpenInfo, staff, now))
            .ToList();
    }

    public static async Task<ClaimDetailDto?> Detail(ClaimsDbContext db, Guid id, ClaimsPrincipal user, TimeProvider clock)
    {
        var claim = await db.Claims.AsNoTracking()
            .Include(c => c.InformationRequests)
            .Include(c => c.Notes)
            .Include(c => c.Activities)
            .AsSplitQuery()
            .SingleOrDefaultAsync(c => c.Id == id);

        var staff = user.IsStaff();
        if (claim is null || (!staff && claim.ClaimantId != user.UserId())) return null;

        var policy = await db.Policies.AsNoTracking().SingleAsync(p => p.Id == claim.PolicyId);

        var userIds = claim.Activities.Select(a => a.ActorId)
            .Concat(claim.Notes.Select(n => n.AuthorId))
            .Append(claim.ClaimantId)
            .Concat(claim.AssignedOfficerId is { } o ? [o] : [])
            .Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => (u.Name, u.IsStaff));

        string ActorName(Guid actorId) =>
            !users.TryGetValue(actorId, out var u) ? "Unknown"
            : u.IsStaff && !staff ? "Claims team"
            : u.Name;

        var now = clock.GetUtcNow().UtcDateTime;
        var openInfo = claim.InformationRequests.Count(r => r.Status == InfoRequestStatus.Open);
        var summary = ToSummary(claim, users[claim.ClaimantId].Name,
            claim.AssignedOfficerId is { } oid ? users[oid].Name : null, openInfo, staff, now);

        return new ClaimDetailDto(
            summary,
            policy.PolicyNumber, claim.Location, claim.Description,
            claim.SettledAmount, claim.DecisionReason, claim.ClosedAt,
            policy.CoverageLimit, policy.Excess,
            staff ? policy.MaxPayable(claim.ClaimedAmount) : null,
            Enum.GetValues<ClaimStatus>().Where(s => ClaimWorkflow.CanTransition(claim.Status, s)).ToList(),
            claim.InformationRequests.OrderBy(r => r.RequestedAt)
                .Select(r => new InfoRequestDto(r.Id, r.Question, r.RequestedAt, r.Response, r.RespondedAt, r.Status)).ToList(),
            staff
                ? claim.Notes.OrderByDescending(n => n.CreatedAt)
                    .Select(n => new NoteDto(n.Id, ActorName(n.AuthorId), n.Text, n.CreatedAt)).ToList()
                : null,
            claim.Activities.Where(a => staff || a.VisibleToClaimant)
                .OrderBy(a => a.At)
                .Select(a => new ActivityDto(a.At, ActorName(a.ActorId), a.Action, a.Detail)).ToList());
    }

    private static ClaimSummaryDto ToSummary(Claim c, string claimantName, string? officerName, int openInfo,
        bool staff, DateTime now) =>
        new(c.Id, c.ClaimNumber, c.Type, c.Market, c.Currency, c.Status,
            c.ClaimedAmount, staff ? c.ReserveAmount : null, c.ApprovedAmount,
            c.IncidentDate, c.SubmittedAt, c.UpdatedAt,
            (int)((c.ClosedAt ?? now) - c.SubmittedAt).TotalDays,
            c.AssignedOfficerId, staff ? officerName : null, claimantName, openInfo);
}
