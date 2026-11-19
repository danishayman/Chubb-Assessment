using Claims.Api.Auth;
using Claims.Domain;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Data;

/// <summary>Demo users, policies and a spread of claims so every screen has something on it.</summary>
public static class Seeder
{
    public const string DemoPassword = "Password123!";

    public static async Task SeedAsync(ClaimsDbContext db, TimeProvider clock, CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(ct)) return;

        var hash = PasswordHasher.Hash(DemoPassword);
        User U(string name, string email, UserRole role) => new(Guid.NewGuid(), name, email, hash, role);

        var aisha = U("Aisha Tan", "aisha@example.com", UserRole.Claimant);
        var raj = U("Raj Kumar", "raj@example.com", UserRole.Claimant);
        var mei = U("Mei Ling Wong", "mei@example.com", UserRole.Claimant);
        var daniel = U("Daniel Lim", "daniel@chubb.test", UserRole.ClaimsOfficer);
        var priya = U("Priya Nair", "priya@chubb.test", UserRole.ClaimsOfficer);
        var sarah = U("Sarah Chen", "sarah@chubb.test", UserRole.Manager);
        db.Users.AddRange(aisha, raj, mei, daniel, priya, sarah);

        var now = clock.GetUtcNow().UtcDateTime;
        var start = DateOnly.FromDateTime(now).AddMonths(-10);
        var end = start.AddYears(1);
        Policy P(string no, User holder, ClaimType type, Market m, string ccy, decimal limit, decimal excess) =>
            new(Guid.NewGuid(), no, holder.Id, type, m, ccy, limit, excess, start, end);

        var aishaMotor = P("MTR-SG-100231", aisha, ClaimType.Motor, Market.SG, "SGD", 80_000m, 600m);
        var aishaHome = P("PRP-SG-200417", aisha, ClaimType.Property, Market.SG, "SGD", 500_000m, 1_000m);
        var rajMotor = P("MTR-MY-100877", raj, ClaimType.Motor, Market.MY, "MYR", 150_000m, 1_500m);
        var meiHome = P("PRP-HK-200995", mei, ClaimType.Property, Market.HK, "HKD", 2_000_000m, 5_000m);
        db.Policies.AddRange(aishaMotor, aishaHome, rajMotor, meiHome);

        DateTime Ago(double days) => now.AddDays(-days);
        DateOnly Day(double days) => DateOnly.FromDateTime(Ago(days));

        // New, unassigned
        var c1 = Claim.Submit(aishaMotor, aisha.Id, Day(2), "Orchard Road, Singapore", "Rear-ended at a traffic light. Bumper and boot damaged.", 4_200m, Ago(1));
        var c2 = Claim.Submit(meiHome, mei.Id, Day(4), "Tai Koo Shing, Hong Kong", "Burst pipe in upstairs flat flooded kitchen and living room.", 68_000m, Ago(3));

        // Under review with Daniel
        var c3 = Claim.Submit(rajMotor, raj.Id, Day(10), "Jalan Ampang, Kuala Lumpur", "Side collision while changing lanes. Driver door and wing mirror.", 9_800m, Ago(9));
        c3.Assign(daniel.Id, Ago(8));
        c3.SetReserve(daniel.Id, 7_500m, rajMotor, Ago(8));
        c3.AddNote(daniel.Id, "Workshop quote looks high for the door. Asked for a second quote.", Ago(7));

        // Waiting on the claimant
        var c4 = Claim.Submit(aishaHome, aisha.Id, Day(21), "Bukit Timah, Singapore", "Laptop and TV stolen during break-in.", 6_500m, Ago(20));
        c4.Assign(priya.Id, Ago(19));
        c4.RequestInformation(priya.Id, "Please upload the police report and receipts for the stolen items.", Ago(18));

        // Approved, not yet paid
        var c5 = Claim.Submit(rajMotor, raj.Id, Day(35), "Penang Bridge", "Windscreen cracked by debris.", 1_900m, Ago(33));
        c5.Assign(daniel.Id, Ago(32));
        c5.Approve(daniel.Id, 400m, rajMotor, "Windscreen covered, excess applies.", Ago(30));

        // Closed
        var c6 = Claim.Submit(aishaMotor, aisha.Id, Day(60), "Changi Airport carpark", "Minor scratch on door.", 450m, Ago(58));
        c6.Assign(priya.Id, Ago(57));
        c6.Reject(priya.Id, "Damage amount is below the policy excess.", Ago(56));

        db.Claims.AddRange(c1, c2, c3, c4, c5, c6);
        await db.SaveChangesAsync(ct);
    }
}
