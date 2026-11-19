using System.Text.Json;
using Claims.Domain;
using Claims.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Data;

public class ClaimsDbContext(DbContextOptions<ClaimsDbContext> options) : DbContext(options)
{
    public static readonly JsonSerializerOptions EventJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public DbSet<User> Users => Set<User>();
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<InformationRequest> InformationRequests => Set<InformationRequest>();
    public DbSet<ClaimNote> ClaimNotes => Set<ClaimNote>();
    public DbSet<ClaimActivity> ClaimActivities => Set<ClaimActivity>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Store enums as text so the database is readable without the code.
        builder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Name).HasMaxLength(200);
        });

        b.Entity<Policy>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.PolicyNumber).IsUnique();
            e.HasIndex(x => x.HolderId);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.HolderId);
        });

        b.Entity<Claim>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.ClaimNumber).IsUnique();
            e.HasIndex(x => new { x.Status, x.AssignedOfficerId });
            e.HasIndex(x => x.ClaimantId);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Location).HasMaxLength(300);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.HasOne<Policy>().WithMany().HasForeignKey(x => x.PolicyId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ClaimantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedOfficerId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(x => x.DomainEvents);
            e.Ignore(x => x.IsOpen);

            e.HasMany(x => x.InformationRequests).WithOne().HasForeignKey(x => x.ClaimId);
            e.HasMany(x => x.Notes).WithOne().HasForeignKey(x => x.ClaimId);
            e.HasMany(x => x.Activities).WithOne().HasForeignKey(x => x.ClaimId);
            e.Navigation(x => x.InformationRequests).HasField("_infoRequests");
            e.Navigation(x => x.Notes).HasField("_notes");
            e.Navigation(x => x.Activities).HasField("_activities");
        });

        // Child keys are set in the domain, so EF must treat new children found on a loaded claim as inserts.
        b.Entity<InformationRequest>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<ClaimNote>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<ClaimActivity>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => new { x.ClaimId, x.At });
        });

        b.Entity<Notification>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => new { x.UserId, x.SourceEventId }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
        });

        b.Entity<OutboxMessage>(e =>
        {
            e.HasIndex(x => x.ProcessedAt);
            e.Property(x => x.Type).HasMaxLength(100);
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var entry in ChangeTracker.Entries<Claim>().ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Entity.Version = Guid.NewGuid();

            foreach (var e in entry.Entity.DomainEvents)
            {
                OutboxMessages.Add(new OutboxMessage
                {
                    EventId = e.EventId,
                    ClaimId = e.ClaimId,
                    Type = e.GetType().Name,
                    Payload = JsonSerializer.Serialize(e, EventJson),
                    OccurredAt = e.OccurredAt,
                });
            }
            entry.Entity.ClearDomainEvents();
        }

        return await base.SaveChangesAsync(ct);
    }
}
