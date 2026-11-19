using System.Security.Claims;
using Claims.Api.Auth;
using Claims.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Features;

public record NotificationDto(Guid Id, Guid ClaimId, string Message, DateTime CreatedAt, bool Read);
public record NotificationListDto(int UnreadCount, IReadOnlyList<NotificationDto> Items);

public static class NotificationEndpoints
{
    public static void MapNotifications(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/notifications").WithTags("Notifications").RequireAuthorization();

        g.MapGet("/", async (ClaimsPrincipal user, ClaimsDbContext db) =>
        {
            var me = user.UserId();
            var mine = db.Notifications.AsNoTracking().Where(n => n.UserId == me);
            var unread = await mine.CountAsync(n => n.ReadAt == null);
            var items = await mine.OrderByDescending(n => n.CreatedAt).Take(50)
                .Select(n => new NotificationDto(n.Id, n.ClaimId, n.Message, n.CreatedAt, n.ReadAt != null))
                .ToListAsync();
            return new NotificationListDto(unread, items);
        });

        g.MapPost("/{id:guid}/read", async (Guid id, ClaimsPrincipal user, ClaimsDbContext db, TimeProvider clock) =>
        {
            var n = await db.Notifications.SingleOrDefaultAsync(x => x.Id == id && x.UserId == user.UserId());
            if (n is null) return Results.NotFound();
            n.MarkRead(clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        g.MapPost("/read-all", async (ClaimsPrincipal user, ClaimsDbContext db, TimeProvider clock) =>
        {
            var me = user.UserId();
            await db.Notifications.Where(n => n.UserId == me && n.ReadAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, clock.GetUtcNow().UtcDateTime));
            return Results.NoContent();
        });
    }
}
