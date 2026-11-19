using System.Security.Claims;
using Claims.Api.Auth;
using Claims.Api.Data;
using Claims.Domain;
using Microsoft.EntityFrameworkCore;
using Claim = Claims.Domain.Claim;

namespace Claims.Api.Features;

public static class ClaimEndpoints
{
    public static void MapClaims(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/policies/mine", async (ClaimsPrincipal user, ClaimsDbContext db) =>
            await db.Policies.Where(p => p.HolderId == user.UserId())
                .OrderBy(p => p.PolicyNumber)
                .Select(p => new PolicyDto(p.Id, p.PolicyNumber, p.Type, p.Market, p.Currency, p.CoverageLimit, p.Excess, p.StartDate, p.EndDate))
                .ToListAsync())
            .WithTags("Policies").RequireAuthorization(Policies.Claimant);

        app.MapGet("/api/staff", async (ClaimsDbContext db) =>
            await db.Users.Where(u => u.Role == UserRole.ClaimsOfficer || u.Role == UserRole.Manager)
                .OrderBy(u => u.Name).Select(u => new StaffDto(u.Id, u.Name, u.Role)).ToListAsync())
            .WithTags("Staff").RequireAuthorization(Policies.Staff);

        var g = app.MapGroup("/api/claims").WithTags("Claims").RequireAuthorization();

        g.MapGet("/", ListClaims);
        g.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, ClaimsDbContext db, TimeProvider clock) =>
            await ClaimQueries.Detail(db, id, user, clock) is { } dto ? Results.Ok(dto) : Results.NotFound());

        g.MapPost("/", SubmitClaim).RequireAuthorization(Policies.Claimant);

        g.MapPost("/{id:guid}/info-requests/{requestId:guid}/response",
            (Guid id, Guid requestId, InfoResponseRequest req, ClaimsPrincipal user, ClaimsDbContext db, TimeProvider clock) =>
                Act(id, user, db, clock, (c, now) => c.ProvideInformation(user.UserId(), requestId, req.Response, now)))
            .RequireAuthorization(Policies.Claimant);

        // Staff workflow actions. Each one loads the aggregate, calls one domain method and saves.
        var staff = g.MapGroup("/{id:guid}").RequireAuthorization(Policies.Staff);

        staff.MapPost("/assign", AssignClaim);
        staff.MapPost("/reserve", async (Guid id, ReserveRequest req, ClaimsPrincipal user, ClaimsDbContext db, TimeProvider clock) =>
            await ActWithPolicy(id, user, db, clock, (c, p, now) => c.SetReserve(user.UserId(), req.Amount, p, now)));
        staff.MapPost("/info-requests", (Guid id, InfoRequestRequest req, ClaimsPrincipal user, ClaimsDbContext db, TimeProvider clock) =>
            Act(id, user, db, clock, (c, now) => c.RequestInformation(user.UserId(), req.Question, now)));
        staff.MapPost("/approve", (Guid id, ApproveRequest req, ClaimsPrincipal user, ClaimsDbContext db, TimeProvider clock) =>
            ActWithPolicy(id, user, db, clock, (c, p, now) => c.Approve(user.UserId(), req.Amount, p, req.Reason, now)));
        staff.MapPost("/reject", (Guid id, RejectRequest req, ClaimsPrincipal user, ClaimsDbContext db, TimeProvider clock) =>
            Act(id, user, db, clock, (c, now) => c.Reject(user.UserId(), req.Reason, now)));
        staff.MapPost("/settle", (Guid id, ClaimsPrincipal user, ClaimsDbContext db, TimeProvider clock) =>
            Act(id, user, db, clock, (c, now) => c.Settle(user.UserId(), now)));
        staff.MapPost("/notes", (Guid id, NoteRequest req, ClaimsPrincipal user, ClaimsDbContext db, TimeProvider clock) =>
            Act(id, user, db, clock, (c, now) => c.AddNote(user.UserId(), req.Text, now)));
    }

    private static async Task<IResult> ListClaims(ClaimsPrincipal user, ClaimsDbContext db, TimeProvider clock,
        ClaimStatus? status, string? assigned, Market? market, bool? openOnly, string? search)
    {
        var q = db.Claims.AsNoTracking();

        if (!user.IsStaff())
            q = q.Where(c => c.ClaimantId == user.UserId());
        else
        {
            var me = user.UserId();
            q = assigned switch
            {
                "me" => q.Where(c => c.AssignedOfficerId == me),
                "unassigned" => q.Where(c => c.AssignedOfficerId == null),
                _ => q,
            };
            if (market is not null) q = q.Where(c => c.Market == market);
        }

        if (status is not null) q = q.Where(c => c.Status == status);
        if (openOnly == true) q = q.Where(c => ClaimWorkflow.OpenStatuses.Contains(c.Status));
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(c => c.ClaimNumber.Contains(search.Trim().ToUpper()));

        var rows = await ClaimQueries.Summaries(db, q.OrderBy(c => c.SubmittedAt).Take(200), user, clock);
        return Results.Ok(rows);
    }

    private static async Task<IResult> SubmitClaim(SubmitClaimRequest req, ClaimsPrincipal user, ClaimsDbContext db,
        TimeProvider clock)
    {
        if ((req.Location ?? "").Length > 300 || (req.Description ?? "").Length > 4000)
            return Results.Problem("Location or description is too long.", statusCode: 400);

        var policy = await db.Policies.FindAsync(req.PolicyId);
        if (policy is null || policy.HolderId != user.UserId())
            return Results.Problem("Policy not found.", statusCode: 404);

        var claim = Claim.Submit(policy, user.UserId(), req.IncidentDate, req.Location ?? "", req.Description ?? "",
            req.ClaimedAmount, clock.GetUtcNow().UtcDateTime);
        db.Claims.Add(claim);
        await db.SaveChangesAsync();

        var dto = await ClaimQueries.Detail(db, claim.Id, user, clock);
        return Results.Created($"/api/claims/{claim.Id}", dto);
    }

    private static async Task<IResult> AssignClaim(Guid id, AssignRequest? req, ClaimsPrincipal user, ClaimsDbContext db,
        TimeProvider clock)
    {
        var officerId = req?.OfficerId ?? user.UserId();
        if (officerId != user.UserId())
        {
            // Officers pick up work for themselves. Only a manager can hand a claim to someone else.
            if (!user.IsManager())
                return Results.Problem("Only a manager can assign a claim to another officer.", statusCode: 403);
            var target = await db.Users.FindAsync(officerId);
            if (target is null || !target.IsStaff)
                return Results.Problem("Officer not found.", statusCode: 400);
        }

        return await Act(id, user, db, clock, (c, now) => c.Assign(officerId, now));
    }

    private static async Task<IResult> Act(Guid id, ClaimsPrincipal user, ClaimsDbContext db, TimeProvider clock,
        Action<Claim, DateTime> action)
    {
        var claim = await db.Claims.Include(c => c.InformationRequests).SingleOrDefaultAsync(c => c.Id == id);
        if (claim is null || (!user.IsStaff() && claim.ClaimantId != user.UserId()))
            return Results.NotFound();

        action(claim, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync();
        return Results.Ok(await ClaimQueries.Detail(db, id, user, clock));
    }

    private static async Task<IResult> ActWithPolicy(Guid id, ClaimsPrincipal user, ClaimsDbContext db,
        TimeProvider clock, Action<Claim, Policy, DateTime> action)
    {
        var policyId = await db.Claims.Where(c => c.Id == id).Select(c => (Guid?)c.PolicyId).SingleOrDefaultAsync();
        if (policyId is null) return Results.NotFound();
        var policy = await db.Policies.FindAsync(policyId.Value);
        return await Act(id, user, db, clock, (c, now) => action(c, policy!, now));
    }
}
