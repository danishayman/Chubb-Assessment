using System.Net;
using System.Net.Http.Json;
using Claims.Api.Features;
using Claims.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Claims.Tests.Api;

public class ClaimsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = ApiFactory.Json;

    private static async Task<T> Read<T>(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<ClaimDetailDto> Submit(HttpClient claimant, decimal amount = 3_000m)
    {
        var policies = await claimant.GetFromJsonAsync<List<PolicyDto>>("/api/policies/mine", Json);
        var motor = policies!.First(p => p.Type == ClaimType.Motor);
        var res = await claimant.PostAsJsonAsync("/api/claims", new SubmitClaimRequest(
            motor.Id, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), "Clementi Ave 3", "Reversed into a bollard.", amount), Json);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return await Read<ClaimDetailDto>(res);
    }

    private static async Task<T> Eventually<T>(Func<Task<T>> get, Func<T, bool> done)
    {
        for (var i = 0; i < 50; i++)
        {
            var value = await get();
            if (done(value)) return value;
            await Task.Delay(100);
        }
        throw new TimeoutException("Condition was not met in time.");
    }

    [Fact]
    public async Task Claim_goes_from_submission_to_settlement_and_claimant_is_notified()
    {
        var claimant = await factory.LoginAs("aisha@example.com");
        var officer = await factory.LoginAs("daniel@chubb.test");

        var claim = await Submit(claimant);
        var id = claim.Summary.Id;
        Assert.Equal(ClaimStatus.Submitted, claim.Summary.Status);

        var unassigned = await officer.GetFromJsonAsync<List<ClaimSummaryDto>>("/api/claims?assigned=unassigned", Json);
        Assert.Contains(unassigned!, c => c.Id == id);

        claim = await Read<ClaimDetailDto>(await officer.PostAsJsonAsync($"/api/claims/{id}/assign", new AssignRequest(null), Json));
        Assert.Equal(ClaimStatus.UnderReview, claim.Summary.Status);

        claim = await Read<ClaimDetailDto>(await officer.PostAsJsonAsync($"/api/claims/{id}/info-requests", new InfoRequestRequest("Photos of the damage please"), Json));
        Assert.Equal(ClaimStatus.AwaitingInformation, claim.Summary.Status);
        var requestId = claim.InformationRequests.Single().Id;

        claim = await Read<ClaimDetailDto>(await claimant.PostAsJsonAsync($"/api/claims/{id}/info-requests/{requestId}/response", new InfoResponseRequest("Uploaded to the portal"), Json));
        Assert.Equal(ClaimStatus.UnderReview, claim.Summary.Status);

        claim = await Read<ClaimDetailDto>(await officer.PostAsJsonAsync($"/api/claims/{id}/approve", new ApproveRequest(2_000m, "Liability accepted"), Json));
        Assert.Equal(ClaimStatus.Approved, claim.Summary.Status);

        claim = await Read<ClaimDetailDto>(await officer.PostAsync($"/api/claims/{id}/settle", null));
        Assert.Equal(ClaimStatus.Settled, claim.Summary.Status);
        Assert.Equal(2_000m, claim.SettledAmount);

        // Notifications are built asynchronously from events, so poll for them.
        var notifications = await Eventually(
            () => claimant.GetFromJsonAsync<NotificationListDto>("/api/notifications", Json)!,
            n => n!.Items.Any(i => i.ClaimId == id && i.Message.Contains("settled")));
        var forClaim = notifications!.Items.Where(i => i.ClaimId == id).ToList();
        Assert.Contains(forClaim, i => i.Message.Contains("received"));
        Assert.Contains(forClaim, i => i.Message.Contains("more information"));
        Assert.Contains(forClaim, i => i.Message.Contains("approved"));

        var officerNotes = await Eventually(
            () => officer.GetFromJsonAsync<NotificationListDto>("/api/notifications", Json)!,
            n => n!.Items.Any(i => i.ClaimId == id));
        Assert.Contains(officerNotes!.Items, i => i.ClaimId == id && i.Message.Contains("responded"));
    }

    [Fact]
    public async Task Claimant_view_hides_reserve_and_internal_notes()
    {
        var claimant = await factory.LoginAs("aisha@example.com");
        var officer = await factory.LoginAs("daniel@chubb.test");
        var id = (await Submit(claimant)).Summary.Id;

        await Read<ClaimDetailDto>(await officer.PostAsJsonAsync($"/api/claims/{id}/assign", new AssignRequest(null), Json));
        await Read<ClaimDetailDto>(await officer.PostAsJsonAsync($"/api/claims/{id}/notes", new NoteRequest("Suspicious, check CCTV"), Json));

        var staffView = await officer.GetFromJsonAsync<ClaimDetailDto>($"/api/claims/{id}", Json);
        Assert.NotNull(staffView!.Summary.ReserveAmount);
        Assert.Single(staffView.Notes!);

        var claimantView = await claimant.GetFromJsonAsync<ClaimDetailDto>($"/api/claims/{id}", Json);
        Assert.Null(claimantView!.Summary.ReserveAmount);
        Assert.Null(claimantView.Notes);
        Assert.DoesNotContain(claimantView.Timeline, t => t.Action == "Assigned");
        Assert.All(claimantView.Timeline.Where(t => t.Action == "StatusChanged"), t => Assert.Equal("Claims team", t.Actor));
    }

    [Fact]
    public async Task Claimants_cannot_see_other_claims_or_use_staff_actions()
    {
        var aisha = await factory.LoginAs("aisha@example.com");
        var raj = await factory.LoginAs("raj@example.com");
        var id = (await Submit(aisha)).Summary.Id;

        Assert.Equal(HttpStatusCode.NotFound, (await raj.GetAsync($"/api/claims/{id}")).StatusCode);
        Assert.DoesNotContain((await raj.GetFromJsonAsync<List<ClaimSummaryDto>>("/api/claims", Json))!, c => c.Id == id);
        Assert.Equal(HttpStatusCode.Forbidden, (await aisha.PostAsJsonAsync($"/api/claims/{id}/assign", new AssignRequest(null), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await aisha.GetAsync("/api/dashboard/exposure")).StatusCode);
    }

    [Fact]
    public async Task Invalid_transition_returns_409_problem()
    {
        var claimant = await factory.LoginAs("aisha@example.com");
        var officer = await factory.LoginAs("daniel@chubb.test");
        var id = (await Submit(claimant)).Summary.Id;
        await Read<ClaimDetailDto>(await officer.PostAsJsonAsync($"/api/claims/{id}/assign", new AssignRequest(null), Json));

        var res = await officer.PostAsync($"/api/claims/{id}/settle", null);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var problem = await res.Content.ReadFromJsonAsync<ProblemDetails>(Json);
        Assert.Contains("Cannot move claim", problem!.Detail);
    }

    [Fact]
    public async Task Only_manager_can_assign_to_someone_else()
    {
        var claimant = await factory.LoginAs("aisha@example.com");
        var daniel = await factory.LoginAs("daniel@chubb.test");
        var manager = await factory.LoginAs("sarah@chubb.test");
        var id = (await Submit(claimant)).Summary.Id;

        var staff = await manager.GetFromJsonAsync<List<StaffDto>>("/api/staff", Json);
        var priya = staff!.Single(s => s.Name == "Priya Nair");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await daniel.PostAsJsonAsync($"/api/claims/{id}/assign", new AssignRequest(priya.Id), Json)).StatusCode);

        var claim = await Read<ClaimDetailDto>(await manager.PostAsJsonAsync($"/api/claims/{id}/assign", new AssignRequest(priya.Id), Json));
        Assert.Equal(priya.Id, claim.Summary.AssignedOfficerId);
    }

    [Fact]
    public async Task Manager_can_act_on_a_claim_another_officer_owns()
    {
        var claimant = await factory.LoginAs("aisha@example.com");
        var daniel = await factory.LoginAs("daniel@chubb.test");
        var priya = await factory.LoginAs("priya@chubb.test");
        var manager = await factory.LoginAs("sarah@chubb.test");
        var id = (await Submit(claimant)).Summary.Id;
        var assigned = await Read<ClaimDetailDto>(await daniel.PostAsJsonAsync($"/api/claims/{id}/assign", new AssignRequest(null), Json));

        Assert.Equal(HttpStatusCode.Forbidden,
            (await priya.PostAsJsonAsync($"/api/claims/{id}/reject", new RejectRequest("Not covered"), Json)).StatusCode);

        var rejected = await Read<ClaimDetailDto>(await manager.PostAsJsonAsync($"/api/claims/{id}/reject", new RejectRequest("Not covered"), Json));
        Assert.Equal(ClaimStatus.Rejected, rejected.Summary.Status);
        Assert.Equal(assigned.Summary.AssignedOfficerId, rejected.Summary.AssignedOfficerId);
        Assert.Contains(rejected.Timeline, t => t.Action == "StatusChanged" && t.Actor == "Sarah Chen");
    }

    [Fact]
    public async Task Exposure_reflects_new_claim_reserve()
    {
        var claimant = await factory.LoginAs("raj@example.com");
        var manager = await factory.LoginAs("sarah@chubb.test");

        decimal MyMotor(ExposureDto e) => e.Lines.Where(l => l.Market == Market.MY && l.Type == ClaimType.Motor).Sum(l => l.Reserved);
        var before = MyMotor((await manager.GetFromJsonAsync<ExposureDto>("/api/dashboard/exposure", Json))!);

        await Submit(claimant, amount: 11_500m); // excess 1,500 -> reserve 10,000

        var after = MyMotor((await manager.GetFromJsonAsync<ExposureDto>("/api/dashboard/exposure", Json))!);
        Assert.Equal(before + 10_000m, after);
    }

    [Fact]
    public async Task Bad_login_is_rejected()
    {
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("aisha@example.com", "wrong"));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
